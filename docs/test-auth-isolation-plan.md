# Analiza CI testova: izolacija autentifikacije i email transporta

## Preporučeni rezultat

Poslovni API testovi ne treba da koriste registraciju, potvrdu adrese, ponovno slanje potvrde ili login kao pripremu podataka. Treba da dobijaju novog korisnika direktno kroz ASP.NET Identity u izolovanoj PostgreSQL testnoj bazi i pravi JWT kroz postojeći `IAuthSessionService`/`ITokenService`.

Slanje stvarnih mejlova može se potpuno ukloniti iz CI i lokalnih automatizovanih testova. Zamjenjuje se transport `IEmailService`, dok stvarni `EmailVerificationService`, Identity tokeni i auth endpointi ostaju dostupni za ciljane auth testove. Nije potrebno mijenjati produkciono ponašanje.

Ovo je analiza i prijedlog refaktorisanja. Testni i produkcioni kod nisu mijenjani, CI nije izvršen i udaljeni CI logovi nisu pregledani. Zaključci se zasnivaju na postojećim fajlovima.

## Šta CI trenutno izvršava

`.github/workflows/ci.yml` se pokreće za pull requestove i push na `main`/`development`. Job `test` koristi `ubuntu-latest`, SDK iz `global.json`, ima limit 15 minuta i poziva:

```sh
dotnet test --configuration Release --logger "trx;LogFileName=results.trx" --results-directory ./TestResults
```

Pošto nema filtera niti eksplicitne putanje projekta, izvršavaju se testni projekti iz solution-a: unit testovi i `SimpleShare.IntegrationTests`. Unit testovi ne trebaju registraciju, mejlove ni PostgreSQL. API testovi dijele jedan `ApiFixture`, in-process host i PostgreSQL 16 Testcontainer kroz `ApiCollection`. Docker je predviđen na CI runneru. Nema potrebe mijenjati tu osnovu niti vraćati se na EF InMemory kao zamjenu za PostgreSQL.

CI konfiguracija ne prosljeđuje Resend API ključ. To ne izoluje email transport: može samo uzrokovati grešku pri njegovom korišćenju. `Testing` okruženje samo po sebi takođe ne isključuje email servis. Lokalno se mogu naslijediti konfiguracija i environment varijable koje omogućavaju stvarni transport.

Jedan fiksni `results.trx` i zajednički izlazni direktorijum za više projekata predstavljaju rizik prepisivanja izvještaja. Naziv izvještaja `integration tests` takođe ne opisuje više cijeli job. Stvarno prepisivanje artefakta nije potvrđeno pregledom CI logova.

## Potvrđeni problemi u kodu

### Registracioni helper više ne odgovara API contractu

`tests/SimpleShare.IntegrationTests/Extensions.cs:RegisterAsync` radi POST na `/api/Auth/register`, očekuje 200, zatim deserijalizuje odgovor kao `LoginUserResponse` i postavlja bearer token.

Produkcioni `AuthService.RegisterAsync` sada:

1. Kreira nepotvrđenog korisnika, dodaje ulogu i kreira njegov `Member`.
2. Poziva `IEmailVerificationService.SendVerificationLinkAsync`.
3. Vraća `RegisterResponse` sa `Email` i `EmailVerificationRequired`, bez JWT-a.

Ako transport zakaže, registracija vraća kontrolisanu grešku i helper pada prije poslovnog testa. Ako slanje uspije, vraćeni JSON nema obavezna polja klase `LoginUserResponse`, pa helper nije kompatibilan ni sa uspješnim odgovorom. Dodavanje login poziva poslije registracije nije rješenje: ispravan password za nepotvrđenu adresu daje 403 `email_not_confirmed`.

Ovo je strukturna neusklađenost utvrđena čitanjem koda, a ne izmjeren rezultat novog CI izvršavanja.

### Veliki broj testova dijeli istu nepovezanu zavisnost

Pronađena su 54 mjesta poziva `fixture.RegisterAsync(...)` u izvornim fajlovima: 48 izvan `AuthTests.cs` i 6 u njemu. To nije broj izvršenih registracija: theories, petlje i ponovna upotreba helpera mogu povećati broj poziva u jednom test run-u.

Pozivi su u group, expense, balance, overall balance, member, invitation, app-link i Simplify Debts testovima. Greška registracije tako obara mnogo testova koji registraciju uopšte ne provjeravaju. To otežava dijagnostiku finansijskih i regresionih problema.

### Pravi email transport nije zamijenjen

`ApiFixture.ConfigureTestServices` trenutno zamjenjuje Google validaciju, ali ne zamjenjuje `IEmailService`. Produkciona registracija `IEmailService -> ResendEmailService` zato ostaje aktivna.

`ResendEmailService` poziva stvarni `IResend.EmailSendAsync`. Poznati aplikacioni putevi prema `IEmailService` jesu verifikacija/resend i Development endpoint za probni email; Development endpoint nije aktivan u Testing hostu. Nepostojeća ili `.test.local` adresa ne sprečava HTTP poziv email provideru. Prazan API ključ nije pouzdan način isključivanja transporta.

### Testna dokumentacija je zastarjela

`docs/api-test-cases.md`, komentar u `ApiFixture` i lokalni `.claude/skills/api-integration-tests/SKILL.md` još zahtijevaju tokene iz registracije. Registracija više ne izdaje tokene. Dokumentacija navodi stare registracione slučajeve i statuse koji nisu dokaz trenutnog pokrića.

U postojećim testovima nisu pronađeni eksplicitni pozivi `/api/Auth/confirm-email` ili `/api/Auth/resend-verification`, niti fake email servis. Stvarni novi auth tok zato nema odgovarajuće eksplicitno pokriće, dok zavisnost od njega ostaje u pripremi drugih testova.

Komentari da registracija ne kreira `Member` takođe ne odgovaraju sadašnjem `EnsureMemberForUserAsync` pozivu. To je važno za testove korisnika bez member identiteta i bez grupa.

## Predlog refaktorisanja

### 1. Uvesti testni email servis koji snima poruke u memoriji

U test projektu dodati `RecordingEmailService : IEmailService`, bez Resend, SMTP ili drugih mrežnih poziva. Snimati adresu primaoca, ime i verification URL. Ne treba praviti email provider emulator niti stvarno HTML sanduče.

U `ConfigureTestServices`:

- `RemoveAll<IEmailService>()`, pa registrovati jednu instancu testnog servisa kao singleton i kao `IEmailService`.
- Zadržati pravi `IEmailVerificationService`. Zamjena tog servisa bi zaobišla generisanje tokena, potvrđivanje i resend pravila koja treba testirati.
- Kao dodatnu zaštitu ukloniti registracije `IResend` iz test hosta, kako bi nenamjeran direktan pristup provideru pao pri DI razrješavanju umjesto da šalje poruke.
- U test konfiguraciji eksplicitno postaviti prazan `Resend:ApiKey`, testni `AppLinks:PublicBaseUrl`, npr. `https://test.local`, i izolovanu Data Protection konfiguraciju. Sam prazan ključ nije osnovna zaštita; zamjena transporta jeste.
- Dodati provjeru da su sve razriješene `IEmailService` instance testne i da pravi `IResend` nije dostupan.

Singleton sanduče mora biti thread-safe. Poruke se izdvajaju po jedinstvenoj adresi konkretnog testa. Ne koristiti globalni `Clear()` ili globalni `FailNextSend`, jer mogu uticati na drugi test. Simulaciju neuspjeha podesiti po primaocu ili u zasebnom hostu. Tokeni i verification URL-ovi ne smiju se ispisivati u CI logove.

Prednost u odnosu na no-op servis: možemo provjeriti da je aplikacija pokušala očekivano slanje, da nema duplikata i da je generisani token validan. Nijedan stvarni email ne odlazi. No-op bi sakrio nedostajuće ili pogrešne pozive slanja.

### 2. Zamijeniti registracioni setup testnom fabrikom korisnika

Dodati `CreateAuthenticatedClientAsync`, sa povratnim oblikom kompatibilnim sa sadašnjim `(HttpClient, LoginUserResponse)` gdje je to praktično. Niže može koristiti mali `CreateUserAsync` helper za specifična auth stanja.

Preporučeni postupak:

1. Pokrenuti fixture host kroz `CreateClient`, kako bi postojeće migracije i role seeding bili završeni.
2. U novom DI scope-u kreirati jedinstven `User` kroz `UserManager<User>`, sa eksplicitnim GUID-om, jedinstvenim emailom, imenima i `EmailConfirmed = true` za obične poslovne testove.
3. Pozvati `UserManager.CreateAsync(user)` bez passworda za testove kojima password nije potreban. Login testovi eksplicitno koriste varijantu sa passwordom. Provjeriti svaki `IdentityResult` i ne ignorisati greške pripreme.
4. Dodijeliti stvarnu ulogu `User` kroz `AddToRoleAsync`; ne davati administratora i ne upisivati Identity tabele ručno.
5. Po potrebi kreirati `Member` kroz postojeći `IMemberService.EnsureMemberForUserAsync`. Preporuka je da normalan potvrđeni korisnik podrazumijevano ima member identitet, uz eksplicitnu varijantu bez njega.
6. Izdati stvarnu sesiju kroz `IAuthSessionService.CreateSessionAsync` i postaviti JWT na klijenta.

Poslovni testovi tako zadržavaju pravi JWT potpis, issuer/audience/expiry provjere, role claims, middleware i provjere pristupa stvarnim podacima. Zaobilazi se samo onboarding kao priprema. Ne pozivati ni registraciju ni login endpoint u ovom helperu.

Važna granica: servis za sesije sam ne provjerava email potvrdu; testna fabrika smije izdati običan klijent samo za namjerno pripremljeno potvrđeno stanje. Test koji provjerava zabranu logina nepotvrđenom korisniku kreira korisnika bez autentifikovanog klijenta i poziva pravi `/login`.

Svaki test i dalje dobija svoje korisnike i podatke. Ne koristiti zajednički nalog iz `DbSeeder`, ne dijeliti klijente i JWT tokene između testova i ne uvoditi brisanje cijele baze poslije svakog testa. Startup seeder trenutno već koristi Identity direktno i ne šalje verifikacione mejlove; nije uzrok problema i nije potrebno širiti zahvat na njegov redizajn.

### 3. Migrirati poslovne testove, uz očuvanje namjere posebnih slučajeva

Zamijeniti 48 mjesta poziva izvan `AuthTests` novim helperom. Grupe, troškovi, uplate, pozivnice i druga ponašanja koja su predmet testiranja i dalje se kreiraju/provjeravaju postojećim API pozivima. Direktni DB/Identity setup služi samo za korisnički identitet.

Posebno pregledati:

- `OverallBalanceTests`: zadržati zasebne slučajeve korisnika bez member identiteta i korisnika sa member identitetom bez grupa; oba imaju nulti saldo.
- Group creation: koristiti varijantu bez member identiteta kada se testira njegovo automatsko kreiranje; inače bi setup sakrio taj dio ponašanja.
- Invitation/cross-user testove: svako lice ostaje odvojen korisnik; isti first/last name parametri se zadržavaju.
- Email/password linking: lokalni nalog pripremiti kroz Identity sa passwordom, sa eksplicitnim confirmed ili unconfirmed stanjem koje je predmet testa.
- Tampered/expired JWT testove: baznog korisnika pripremiti novim helperom; kvarenje potpisa i istek i dalje moraju završiti kao 401 kroz pravi middleware.

Stari naziv `RegisterAsync` ne treba zadržati za helper koji više ne radi registraciju. Time bi budući autori ponovo pogrešno pretpostavili šta setup garantuje.

### 4. Ostaviti mali, eksplicitan skup pravih auth HTTP testova

| Oblast | Priprema i provjera |
| --- | --- |
| Registracija | Pravi POST register; `RegisterResponse`, bez JWT-a; nepotvrđen korisnik, uloga i member; tačno jedna snimljena poruka. |
| Neispravna registracija | Password mismatch/policy/validation vraćaju stvarne kodove, bez poruke. |
| Ponovna registracija | Nepotvrđen nalog dobija novu poruku, bez duplog naloga i bez izmjene ranijeg passworda/profila. Potvrđen duplikat ima odgovarajuću grešku. |
| Potvrda adrese | Iz fake sandučeta pročitati URL, izdvojiti `userId` i encoded `token`, pozvati pravi confirm-email i provjeriti Identity stanje. Nema otvaranja eksternog linka ili čekanja stvarnog inboxa. |
| Nevažeća/ponovljena potvrda | Provjeriti invalid_token i postojeću idempotentnost potvrđene adrese. |
| Login | Potvrđen nalog sa passwordom dobija JWT; pogrešan password i nepoznat email imaju isti 401; ispravan password nepotvrđenog naloga daje 403. Nema potrebe za registracionim setupom. |
| Resend | Nepotvrđen nalog dobija poruku; nepoznat/potvrđen nalog ne dobija poruku; neposredni ponovni poziv poštuje cooldown, uz iste javne odgovore gdje je predviđeno. |
| Transport failure | Fake servis deterministički odbije slanje; provjeriti verification_email_failed, očuvan nalog i mogućnost retry-ja. Bez stvarne mreže i automatskog potvrđivanja. |
| Cijeli auth tok | Jedan ciljano zadržan register → snimljeni link → confirm → login → protected GET scenario dokazuje povezivanje komponenti. |
| Google | Zadržati postojeći Google fake i testove stvarnog linking/session toka; ne koristiti Google endpoint kao zamjenski setup za sve poslovne testove. |

Ne treba svaki auth test da prolazi cijeli onboarding. Testirati samo relevantni korak uz kontrolisano Identity početno stanje. Cooldown i rokove ne testirati stvarnim višeminutnim čekanjem; za vremenske granice koristiti ciljane unit testove sa kontrolisanim vremenom ili poseban testni setup ako je potreban.

### 5. Učiniti CI rezultate jasnim i nezavisnim od email providera

Minimalna izmjena je odvojiti testne komande po projektima, uz zadržavanje imena required job-a `test`:

```sh
dotnet build --configuration Release
dotnet test tests/SimpleShare.UnitTests/SimpleShare.UnitTests.csproj --configuration Release --no-build --logger "trx;LogFileName=unit.trx" --results-directory ./TestResults/unit
dotnet test tests/SimpleShare.IntegrationTests/SimpleShare.IntegrationTests.csproj --configuration Release --no-build --logger "trx;LogFileName=integration.trx" --results-directory ./TestResults/integration
```

Koristiti odvojene CI korake i reporter/artifact putanju `TestResults/**/*.trx`. Naziv izvještaja treba da obuhvati oba projekta. Nisu potrebni Resend/Google produkcioni ključevi, SMTP, DNS verifikacija domena ili prava korisnička sandučad. PostgreSQL i dalje ostaje stvarna testna zavisnost.

Odvojeni unit/integration jobovi su moguća kasnija optimizacija, ali nisu potrebni za rješavanje ovog problema. Ne preskakati auth testove u CI samo da bi ostali testovi prošli. Testovi email provider adaptera, ako se dodaju, treba da koriste lažni HTTP handler/provider odgovor, ne stvarni Resend API.

## Alternative i preporuka

| Pristup | Prednost | Nedostatak / odluka |
| --- | --- | --- |
| Identity setup + stvarni session/JWT | Malo izmjena, izoluje onboarding, čuva autentifikacioni middleware i integritet Identity podataka. | Setup koristi aplikacione servise; stvarni login mora imati posebne HTTP testove. **Preporučeno.** |
| Test authentication handler za sve poslovne testove | Jednostavni claims, bez JWT izdavanja. | Zaobilazi JWT validaciju, može sakriti autorizacione regresije i traži odvojene hostove za auth testove. Nije prvi izbor ovdje. |
| Register + fake email + confirm + login za svaki test | Čitav javni onboarding je pokriven. | I dalje spaja sve testove sa više nepovezanih endpointa, tokenima i password hashingom. Zadržati samo jedan ciljani smoke test. |
| Jedan zajednički unaprijed kreiran nalog | Najmanje početnog setupa. | Dijeli podatke, kvari overall/group count i cross-tenant testove, uvodi zavisnost od redoslijeda. Odbaciti. |
| Automatski potvrđivati sve naloge ili zaobići auth u produkcionom kodu kada je Testing | Brzo prikrije trenutni problem. | Testira drugačije poslovno ponašanje i može sakriti sigurnosne greške. Odbaciti. |

## Fajlovi i redoslijed buduće implementacije

1. Dodati test-only `RecordingEmailService` i provjere DI izolacije u `ApiFixture`.
2. Dodati Identity korisničku fabriku/helper i obezbijediti potvrđeno/nepotvrđeno, sa/bez passworda i sa/bez member identiteta stanje.
3. Migrirati poslovne testove i 6 setup poziva u `AuthTests` prema njihovoj namjeri; ne mijenjati očekivane finansijske rezultate.
4. Dodati/obnoviti eksplicitne registration, confirmation, resend i login testove uz stvarne Identity tokene i memorijsko sanduče.
5. Uskladiti `docs/api-test-cases.md`, komentare i lokalni skill sa novom strategijom. Staro pravilo o tokenu iz registracije ne treba zadržati kao prepreku izričito traženom refaktoringu.
6. Razdvojiti CI rezultate; pokrenuti build, sve unit testove i puni API skup sa dostupnim Dockerom.

Kriterijumi prihvatanja: nijedan običan poslovni test ne poziva register/confirm/resend/login radi pripreme; nijedan test ne šalje stvarni email; javni auth tok ostaje eksplicitno testiran; korisnici su jedinstveni po testu; pravi JWT i provjere pristupa ostaju; neusklađeni finansijski podaci i dalje daju postojeće kontrolisane greške; svi TRX izvještaji ostaju sačuvani. Ubrzanje nije mjereno i ne treba ga izražavati procentima prije poređenja istog CI run-a prije/poslije.

## Status implementacije

Plan je implementiran u testnom projektu i CI konfiguraciji. `CreateAuthenticatedClientAsync` koristi Identity i pravi session servis; poslovni setup nema register/confirm/resend/login pozive. Login i Google-link testovi koji zahtijevaju lokalni password eksplicitno ga traže. Zadržani su odvojeni slučajevi korisnika bez member identiteta i korisnika sa member identitetom bez grupa.

`RecordingEmailService` je jedini email transport testnog hosta, sa thread-safe sandučetom i greškama izolovanim po primaocu. `IResend` nije registrovan u tom hostu. `EmailAuthTests` ima 12 slučajeva za izolaciju transporta, puni onboarding, neispravnu registraciju, resend/cooldown, nevažeći token, potvrđeni duplikat, zabranu logina nepotvrđenog korisnika i retry poslije neuspjelog slanja.

Release build je uspješan; svih 51 postojećih unit testova prolazi (0 neuspješnih, 0 preskočenih). Novih 12 auth slučajeva je kompajlirano i otkriveno. Integracioni testovi nisu izvršeni jer lokalni Docker daemon nije dostupan. Njihov prolaz i stvarno CI izvršavanje još nisu potvrđeni. Ostala su postojeća build upozorenja, uključujući NU1903 za transitive `Microsoft.AspNetCore.WebUtilities` 8.0.20.

Produkcioni auth/email servisi, API contract i baza nisu mijenjani. CI unit i integration komande sada pišu zasebne TRX fajlove u odvojene direktorijume; required job i dalje se zove `test`.
