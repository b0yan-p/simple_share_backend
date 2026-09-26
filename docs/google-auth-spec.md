# Google Sign-In — specifikacija za backend

Status: **implementirano** (2026-09-26): §1–§7 i testovi. §8 nije implementiran.
Test ID-jevi su u `docs/api-test-cases.md` pomjereni na `AUTH-12..20`, jer su `AUTH-01..11` već zauzeti.
Implementacija se po strukturi razlikuje od koda u §4–§6, dok ponašanje ostaje isto:
- `GoogleUserInfo` je zamijenjen sa `ExternalIdentity`, koji ne zavisi od providera.
- Logika naloga (find/link/create/rola) je u `ExternalAccountService`.
- Member se kreira kroz `MemberService.EnsureMemberForUserAsync`.
- Kontroler samo orkestrira te pozive.
Datum: 2026-09-26

Cilj: mobilna aplikacija dobija Google identitet, šalje ga SimpleShare API-ju, a API vraća
**potpuno isti** `LoginUserResponse` koji već vraćaju `/api/Auth/login` i `/api/Auth/register`.
Mobilna aplikacija ne mora znati da li je iza toga bio login ili registracija.

---

## 0. Šta je već u kodu (polazna tačka)

Provjereno u `simpleShare_backend`:

- `Controllers/AuthController.cs` — `register` i `login` vraćaju **identičan** DTO:
  `Contracts/Auth/LoginUserResponse.cs` (`Id`, `FirstName`, `LastName`, `FullName`, `Token`, `Email`).
  Dakle odgovor na potvrdu iz zahtjeva: **da, isti su** i Google endpoint zadržava isti oblik.
- `Services/TokenService.cs` — JWT se pravi iz `(User, IList<string> roles)`, trajanje 7 dana.
  Ništa se ne mijenja, Google korisnik dobija isti token kao i svaki drugi.
- ASP.NET Identity je već konfigurisan (`Program.cs`), tabela **`UserLogins`**
  (`IdentityUserLogin<Guid>`, mapirana u `ApplicationDbContext.OnModelCreating`) već postoji
  u migracijama. To je mjesto gdje se čuva veza `user ↔ google sub` — **nova migracija nije potrebna**.
- Registracija danas radi tri stvari koje Google flow mora ponoviti u istom redoslijedu:
  1. `UserManager.CreateAsync`
  2. `UserManager.AddToRoleAsync(user, "User")`
  3. `MemberService.CreateMemberFromUserAsync(user)` + `repo.SaveChangesAsync()`

---

## 1. Tok (high level)

```
Mobilna (Capacitor Google Sign-In)
   │  native Google dijalog → idToken (JWT potpisan od Google-a)
   ▼
POST /api/Auth/google   { idToken }
   │
   ├─► Google: validacija idToken-a (potpis preko Google JWKS + provjera aud/iss/exp)
   │      ◄── claims: sub, email, email_verified, given_name, family_name, name, picture
   │
   ├─ 1. postoji UserLogin (Google, sub)?        → LOGIN
   ├─ 2. postoji User sa tim emailom?            → LINK + LOGIN
   └─ 3. ništa od toga                           → REGISTRACIJA (User + rola + Member)
   │
   ▼
200 OK  LoginUserResponse  (isti DTO kao /login i /register)
```

---

## 2. Konfiguracija

U `appsettings.json` (vrijednosti se dodaju naknadno, u `appsettings.Development.json`
odnosno u environment varijable / secret store za produkciju):

```jsonc
"Google": {
  // Web / "server" OAuth client — ovo je audience koji backend PRIHVATA.
  "ClientId": "<GOOGLE_CLIENT_ID>",

  // Koristi se SAMO ako se ide na varijantu sa serverAuthCode (vidi §8).
  // Za osnovni idToken flow backend-u NIJE potreban.
  "ClientSecret": "<GOOGLE_CLIENT_SECRET>",

  // Android/iOS klijenti imaju svoje ID-jeve; idToken izdat mobilnom klijentu
  // ima njihov `aud`, pa svi validni audience-i moraju biti nabrojani.
  "AllowedAudiences": [
    "<GOOGLE_CLIENT_ID>",
    "<GOOGLE_ANDROID_CLIENT_ID>",
    "<GOOGLE_IOS_CLIENT_ID>"
  ],

  // Authorized redirect URI — mora biti upisan i u Google Cloud Console,
  // na istom Web OAuth klijentu kao <GOOGLE_CLIENT_ID>, znak za znak.
  // Koristi se samo u serverAuthCode / web varijanti (vidi §8).
  "RedirectUri": "https://api.shareorb.app/api/auth/google/callback"
}
```

Napomene:
- `<GOOGLE_CLIENT_SECRET>` **ne smije** ići u repo niti u mobilnu aplikaciju — samo na backend.
- Redirect URI je **`https://api.shareorb.app/api/auth/google/callback`**. Osnovni `idToken`
  flow iz §1–§6 ga ne koristi (native Google dijalog ne radi HTTP redirect), ali mora biti
  registrovan na Web klijentu da bi §8 i eventualni web login radili. Google poredi string
  doslovno — bez trailing `/`, `http` umjesto `https` ili drugog hosta.
- Ako mobilna traži `idToken` sa `serverClientId = <GOOGLE_CLIENT_ID>`, tada je `aud` uvijek
  web client id i lista `AllowedAudiences` može imati samo jednu vrijednost.
  Preporuka: ići tim putem, jednostavnije je.

Klasa za vezivanje (novi fajl `Common/Security/GoogleAuthOptions.cs`, uz postojeći `Common/Security`):

```csharp
public sealed class GoogleAuthOptions
{
    public const string SectionName = "Google";
    public string ClientId { get; set; } = string.Empty;       // <GOOGLE_CLIENT_ID>
    public string ClientSecret { get; set; } = string.Empty;   // <GOOGLE_CLIENT_SECRET>
    public string[] AllowedAudiences { get; set; } = [];

    // https://api.shareorb.app/api/auth/google/callback
    public string RedirectUri { get; set; } = string.Empty;
}
```

Registracija u `Program.cs`:

```csharp
builder.Services.Configure<GoogleAuthOptions>(
    builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();
```

---

## 3. Paket

```
dotnet add package Google.Apis.Auth
```

`GoogleJsonWebSignature.ValidateAsync` sam skida i kešira Google JWKS ključeve, provjerava
potpis, `iss` (`accounts.google.com` / `https://accounts.google.com`), `exp` i `aud`
prema `ValidationSettings.Audience`. Ne treba ručno zvati `tokeninfo` endpoint.

> Alternativa bez paketa: `GET https://oauth2.googleapis.com/tokeninfo?id_token=...` preko
> `HttpClient`. Radi, ali je jedan mrežni poziv po loginu i Google ga ne preporučuje za
> produkciju. Ide se sa `Google.Apis.Auth`.

---

## 4. Ugovori (Contracts/Auth)

Novi fajl `Contracts/Auth/GoogleLoginRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace simpleShare_backend.Contracts.Auth
{
    public class GoogleLoginRequest
    {
        [Required]
        public required string IdToken { get; set; }
    }
}
```

**Response se ne mijenja** — koristi se postojeći `LoginUserResponse`.

Interni model (nije HTTP ugovor), `Contracts/Auth/GooglePayload.cs` ili kao record u servisu:

```csharp
public sealed record GoogleUserInfo(
    string Subject,       // Google `sub` — stabilan, nikad se ne mijenja
    string Email,
    bool EmailVerified,
    string? GivenName,
    string? FamilyName,
    string? Name);
```

---

## 5. `IGoogleAuthService` — validacija tokena

Novi fajlovi: `Services/Interfaces/IGoogleAuthService.cs` i `Services/GoogleAuthService.cs`.

```csharp
public interface IGoogleAuthService
{
    Task<GoogleUserInfo> ValidateIdTokenAsync(string idToken, CancellationToken ct = default);
}
```

Implementacija:

```csharp
public class GoogleAuthService(IOptions<GoogleAuthOptions> options) : IGoogleAuthService
{
    private readonly GoogleAuthOptions _options = options.Value;

    public async Task<GoogleUserInfo> ValidateIdTokenAsync(string idToken, CancellationToken ct = default)
    {
        var audiences = _options.AllowedAudiences.Length > 0
            ? _options.AllowedAudiences
            : [_options.ClientId];           // fallback na <GOOGLE_CLIENT_ID>

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = audiences });
        }
        catch (InvalidJwtException)
        {
            // istekao, loš potpis, pogrešan aud/iss
            throw new ValidationAppException("google_token_invalid", "Invalid Google token");
        }

        if (string.IsNullOrWhiteSpace(payload.Email))
            throw new ValidationAppException("google_email_missing", "Google account has no email");

        if (payload.EmailVerified != true)
            throw new ValidationAppException("google_email_unverified", "Google email is not verified");

        return new GoogleUserInfo(
            payload.Subject,
            payload.Email,
            payload.EmailVerified ?? false,
            payload.GivenName,
            payload.FamilyName,
            payload.Name);
    }
}
```

`email_verified == true` je **obavezan uslov** — bez njega je §6 korak 2 (spajanje po emailu)
put za preuzimanje tuđeg naloga.

---

## 6. Endpoint `POST /api/Auth/google`

Dodaje se u postojeći `AuthController` (anoniman, kao `login` i `register`).
`AuthController` dobija dva nova dependency-ja: `IGoogleAuthService` i `SignInManager` nije
potreban — sve ide preko `UserManager`.

Konstanta provider-a:

```csharp
private const string GoogleProvider = "Google";
```

### Logika, korak po korak

```csharp
[HttpPost("google")]
public async Task<IActionResult> GoogleLoginAsync([FromBody] GoogleLoginRequest input)
{
    if (!ModelState.IsValid)
        return BadRequest(ModelState);

    // 1) Validacija kod Google-a
    var googleUser = await _googleAuthService.ValidateIdTokenAsync(input.IdToken);

    // 2) Postoji li već vezani nalog? (tabela UserLogins)
    var user = await _userManager.FindByLoginAsync(GoogleProvider, googleUser.Subject);

    if (user is null)
    {
        // 3) Postoji li lokalni nalog sa istim (verifikovanim) emailom → LINK
        user = await _userManager.FindByEmailAsync(googleUser.Email);

        if (user is not null)
        {
            var link = await _userManager.AddLoginAsync(
                user,
                new UserLoginInfo(GoogleProvider, googleUser.Subject, GoogleProvider));

            if (!link.Succeeded)
                return StatusCode((int)HttpStatusCode.InternalServerError, link.Errors);
        }
        else
        {
            // 4) REGISTRACIJA — isti redoslijed kao RegisterAsync
            user = new User
            {
                FirstName = ResolveFirstName(googleUser),
                LastName  = ResolveLastName(googleUser),
                Email     = googleUser.Email,
                UserName  = googleUser.Email,
                EmailConfirmed = true          // Google je već potvrdio email
            };

            // BEZ lozinke — CreateAsync(user) preopterećenje bez password-a
            var createUser = await _userManager.CreateAsync(user);
            if (!createUser.Succeeded) return BadRequest(createUser.Errors);

            var roleResult = await _userManager.AddToRoleAsync(user, "User");
            if (!roleResult.Succeeded)
                return StatusCode((int)HttpStatusCode.InternalServerError, roleResult.Errors);

            var link = await _userManager.AddLoginAsync(
                user,
                new UserLoginInfo(GoogleProvider, googleUser.Subject, GoogleProvider));
            if (!link.Succeeded)
                return StatusCode((int)HttpStatusCode.InternalServerError, link.Errors);
        }
    }

    // 5) Member — isti guard kao u RegisterAsync, pokriva i link i register granu
    var existingMember = await _memberService.GetMemberByUserIdAsync(user.Id);
    if (existingMember is null)
    {
        await _memberService.CreateMemberFromUserAsync(user);
        await _repo.SaveChangesAsync();
    }

    // 6) Isti odgovor kao /login i /register
    var roles = await _userManager.GetRolesAsync(user);
    return Ok(new LoginUserResponse
    {
        Id = user.Id,
        Email = user.Email!,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Token = _tokenService.CreateToken(user, roles)
    });
}
```

### Izvođenje imena

`Member.DisplayName` ima `[StringLength(80, MinimumLength = 2)]`, a `User.FirstName/LastName`
su `string` (ne-nullable, default `""`). Google ne garantuje `given_name`/`family_name`
(npr. nalozi sa samo jednim imenom), pa je potreban fallback:

- `FirstName` = `given_name` → ako prazno, prvi token iz `name` → ako prazno, dio emaila prije `@`
- `LastName`  = `family_name` → ako prazno, ostatak `name` → ako prazno, prazan string
- Oba trimovati i sjeći na 40 karaktera, da `DisplayName` (`"{FirstName} {LastName}"`) ne pređe 80.
- Ako bi `DisplayName` ispao kraći od 2 karaktera, koristiti dio emaila prije `@`.

Ovo ide u privatne helper metode `ResolveFirstName` / `ResolveLastName` u kontroleru,
ili čistije — u `GoogleAuthService` da kontroler ostane tanak.

---

## 7. Ponašanje i rubni slučajevi

| Slučaj | Ponašanje | HTTP |
|---|---|---|
| Validan token, korisnik već vezan | login | `200` + `LoginUserResponse` |
| Validan token, korisnik ne postoji | registracija (User + rola `User` + `Member`) | `200` + `LoginUserResponse` |
| Validan token, postoji lokalni nalog sa istim emailom | veže Google na postojeći nalog i loguje ga | `200` + `LoginUserResponse` |
| `idToken` prazan / nije poslat | `ModelState` greška preko `InvalidModelStateResponseFactory` | `400`, `code: validation_error` |
| Token istekao / loš potpis / pogrešan `aud` | `ValidationAppException` | `400`, `code: google_token_invalid` |
| `email_verified != true` | odbija se | `400`, `code: google_email_unverified` |
| Google nalog bez emaila | odbija se | `400`, `code: google_email_missing` |
| Korisnik registrovan preko Google-a pokuša `/login` sa lozinkom | `CheckPasswordAsync` vraća `false` (nema `PasswordHash`) → postojeća poruka | `401` |

Odluke koje ova specifikacija donosi, da budu eksplicitne:

1. **Auto-linkovanje po emailu je dozvoljeno.** Sigurno je samo zato što je `email_verified`
   tvrd uslov. Alternativa (vraćati `409 email_already_registered`) je sigurnija na papiru ali
   pravi mrtvu ulicu za korisnika koji se prvo registrovao lozinkom.
2. **Google korisnik nema lozinku.** Ne generiše se nasumična lozinka. Ako se kasnije uvede
   "postavi lozinku", to je `UserManager.AddPasswordAsync` i zaseban feature.
3. **Ključ je `sub`, ne email.** Google email se može promijeniti; `sub` ne može.
4. **Nema refresh tokena.** Postojeći JWT od 7 dana ostaje jedini mehanizam — mijenjati ga
   nije dio ovog feature-a.
5. **Google `picture` se ignoriše.** `Member` nema polje za avatar; dodavanje bi bilo scope creep.

### Poznati rizik: pre-hijacking preko `/register` (prihvaćen privremeno)

`/register` ne potvrđuje email. Napadač može registrovati tuđi email sa svojom lozinkom, a kad se
vlasnik kasnije prijavi Google-om, auto-link ga veže na taj nalog. Napadač pritom i dalje zna
lozinku. Rizik je prihvaćen jer se email/pass registracija uklanja u sljedećem koraku i tada nestaje.

### Poznato ograničenje (van obima, ali vrijedi znati)

Ni postojeći `/register` ni ovaj novi endpoint **ne preuzimaju "virtuelnog" `Member`-a**
koji je već kreiran sa istim emailom kroz `GetOrCreateVirtualMemberAsync`
(`Services/MemberService.cs`). `Member` ima unique indeks na `NormalizedEmail`
(`ApplicationDbContext`), pa će `CreateMemberFromUserAsync` u tom slučaju puknuti na
unique violation → `GlobalExceptionHandler` to mapira u `409 unique_constraint`.
To je **postojeće** ponašanje `/register`-a i ovaj feature ga niti popravlja niti pogoršava.
Ako se rješava, rješava se na jednom mjestu za oba endpointa (zaseban task).

---

## 8. Varijanta sa `serverAuthCode` (kad `CLIENT_SECRET` zaista treba)

Osnovni flow iz §1–§6 **ne koristi `<GOOGLE_CLIENT_SECRET>`** — `idToken` je samopotvrđujući.

`<GOOGLE_CLIENT_SECRET>` postaje neophodan tek ako backend treba **offline pristup**
Google API-jima u ime korisnika (npr. čitanje kontakata radi predlaganja članova grupe).
Tada mobilna traži `serverAuthCode` i šalje ga umjesto/uz `idToken`, a backend ga mijenja za
tokene:

```
POST https://oauth2.googleapis.com/token
  code=<serverAuthCode>
  client_id=<GOOGLE_CLIENT_ID>
  client_secret=<GOOGLE_CLIENT_SECRET>
  grant_type=authorization_code
  redirect_uri=https://api.shareorb.app/api/auth/google/callback
```

`redirect_uri` mora biti identičan onom koji je korišten pri traženju `code`-a i onom
upisanom u Google Cloud Console (`GoogleAuthOptions.RedirectUri`); svako odstupanje vraća
`redirect_uri_mismatch`. Za čisto native tok sa `serverAuthCode` Google dozvoljava i prazan
`redirect_uri` — ovdje se ipak drži ista, registrovana vrijednost, da postoji samo jedan URL
za održavanje.

Ako se ikad doda i **web** Google login (browser redirect umjesto native dijaloga), taj isti
URL je i ruta koju backend mora izložiti:

```
GET /api/auth/google/callback?code=...&state=...
```

Ta ruta bi razmijenila `code` za tokene gornjim pozivom, prošla kroz istu logiku iz §6
i vratila isti `LoginUserResponse`. **Nije dio ovog taska** — rutu ne treba implementirati
dok web login ne postane zahtjev; URL se sada samo rezerviše i registruje kod Google-a.

Odgovor sadrži `id_token`, `access_token` i `refresh_token`. `refresh_token` bi se morao
čuvati enkriptovano uz korisnika — to je nova tabela i novi sigurnosni zahtjev.

**Preporuka: ne raditi ovo sada.** Implementirati §1–§6; ostaviti `ClientSecret` u
konfiguraciji kao prazan/pripremljen ključ dok se ne pojavi konkretna potreba.

---

## 9. Šta mobilna aplikacija treba (kontekst, nije predmet ovog taska)

- Plugin za native Google Sign-In (Capacitor), konfigurisan sa
  `serverClientId = <GOOGLE_CLIENT_ID>` kako bi `idToken` imao očekivani `aud`.
- Android: SHA-1/SHA-256 otisak potpisa upisan u Google Cloud Console za `app.shareorb.simpleshare`
  (otisak već stoji u `appsettings.json` → `AppLinks.AndroidSha256Fingerprints`).
- `AuthService.googleLogin(idToken)` — novi `POST` na `${baseAPIUrl}/auth/google`, koji gađa
  **isti** `LoginUser` model i isti `tokenStorage` tok kao `login()` i `register()`.
  Bez novog modela odgovora.

---

## 10. Lista izmjena na backendu

| # | Fajl | Akcija |
|---|---|---|
| 1 | `simpleShare_backend.csproj` | dodati `Google.Apis.Auth` |
| 2 | `appsettings.json` / `appsettings.Development.json` | sekcija `Google` sa `<GOOGLE_CLIENT_ID>`, `<GOOGLE_CLIENT_SECRET>`, `AllowedAudiences`, `RedirectUri` |
| 3 | `Common/Security/GoogleAuthOptions.cs` | **novo** — options klasa |
| 4 | `Contracts/Auth/GoogleLoginRequest.cs` | **novo** — `{ IdToken }` |
| 5 | `Services/Interfaces/IGoogleAuthService.cs` | **novo** |
| 6 | `Services/GoogleAuthService.cs` | **novo** — validacija tokena + izvođenje imena |
| 7 | `Controllers/AuthController.cs` | **izmjena** — novi `POST google`, injektovati `IGoogleAuthService` |
| 8 | `Program.cs` | **izmjena** — `Configure<GoogleAuthOptions>` + `AddScoped<IGoogleAuthService, GoogleAuthService>` |
| 9 | `docs/api-test-cases.md` | **izmjena** — nova ruta u inventaru + `AUTH-11..16` |

Migracija baze: **nije potrebna.** `UserLogins` tabela već postoji u migraciji
`20260728135449_InitPostgres`.
`LoginUserResponse`: **ne dira se.**

---

## 11. Test slučajevi za `docs/api-test-cases.md`

Google validacija se u integracionim testovima zamjenjuje test-double-om za
`IGoogleAuthService` (pravi Google token se ne može dobiti u CI-ju). `AUTH-11` je jedini
koji dira pravu validaciju, i to preko očito neispravnog tokena.

| ID | Slučaj | Očekivano |
|---|---|---|
| `AUTH-11` | `POST /api/Auth/google` sa besmislenim `idToken` | `400`, `code: google_token_invalid` |
| `AUTH-12` | Prvi Google login za nepoznat email | `200`, kreiran `User`, rola `User`, `Member`, token nije prazan |
| `AUTH-13` | Drugi Google login sa istim `sub` | `200`, **isti `Id`** kao u `AUTH-12`, nema novog `Member`-a |
| `AUTH-14` | Google login za email koji je već registrovan preko `/register` | `200`, isti `Id` kao lokalni nalog, red dodat u `UserLogins` |
| `AUTH-15` | Google payload sa `email_verified = false` | `400`, `code: google_email_unverified` |
| `AUTH-16` | `/login` sa lozinkom za nalog napravljen kroz Google | `401`, tijelo identično `AUTH-06` |
| `AUTH-17` | Google payload bez `given_name`/`family_name` (samo `name`) | `200`, `DisplayName` validan (2–80 karaktera) |

Takođe: u tabeli "Endpoint inventory" dodati red
`| POST | /api/Auth/google | anonymous | AUTH-11..17 |`.
