# Faza 3: integraciona i regresiona validacija

## Rezultati izvršavanja

| Provjera | Rezultat |
| --- | --- |
| `dotnet build --no-restore --disable-build-servers -m:1` | Uspješan, bez grešaka. |
| Svi unit testovi | 51 uspješan, 0 neuspješnih, 0 preskočenih. |
| Relevantni API testovi | 61 slučaj kompajliran i otkriven; nisu izvršeni. |
| PostgreSQL Testcontainers | Blokirano: lokalni Docker daemon nije dostupan. |
| `git diff --check` | Bez grešaka. |

API testovi nisu označeni kao uspješni ili preskočeni: njihov fixture nije ni pokrenut. Otkrivanje testova (`--list-tests`) ne izvršava API, SQL ili HTTP provjere. Integracioni dio Faze 3 ostaje nepotvrđen dok se testovi ne pokrenu sa dostupnim Dockerom.

Komande za ponavljanje:

```sh
dotnet build
dotnet test tests/SimpleShare.UnitTests/SimpleShare.UnitTests.csproj --no-build
dotnet test tests/SimpleShare.IntegrationTests/SimpleShare.IntegrationTests.csproj --no-build --filter 'FullyQualifiedName~BalanceTests|FullyQualifiedName~GroupTests|FullyQualifiedName~ExpenseTests|FullyQualifiedName~GroupMemberTests|FullyQualifiedName~SimplifyDebts'
```

Posljednja komanda zahtijeva Docker. Testovi koriste izolovani PostgreSQL container iz postojećeg `ApiFixture`, a ne produkcionu bazu.

## Konkretni graf i finansijske invarijante

Za originalne veze B → N: 8.67, M → N: 0.66, M → B: 10.00, J → B: 25.00 i J → M: 15.00, izvršeni unit test potvrđuje:

| Član | Neto u oba režima |
| --- | --- |
| B | +26.33 |
| N | +9.33 |
| M | +4.34 |
| J | -40.00 |

Regularni rezultat ima originalnih pet veza. Pojednostavljeni ima tačno J → B: 26.33, J → N: 9.33 i J → M: 4.34. Očekivanja nisu mijenjana radi prolaska testa.

Izvršene unit provjere potvrđuju nulti zbir, jednakost neta svakog člana sa ulaznim minus izlaznim vezama, pozitivan cijeli broj centi za svaku vezu, acikličnost oba grafa, determinističnost i granicu broja pojednostavljenih veza. Postojeći testovi nezavisnih komponenti ostaju uključeni.

Dodati su unit testovi za puno poravnanje ovog grafa, uklanjanje jedne stvarne uplate iz obračuna, zastarjeli iznos i ponovljenu zastarjelu vezu. Za puno poravnanje oba grafa su prazna. Ako se ukloni uplata J → N: 9.33, samo J i N ponovo imaju nenulti saldo; pojednostavljeni rezultat je J → N: 9.33.

## Pripremljene API regresije

U `SimplifyDebtsPhase3Tests` dodato je šest API/DB testova:

1. Tačan traženi graf, očuvanje salda, regularnih veza i strukture odgovora pri prebacivanju režima.
2. Puno poravnanje, uključujući novu vezu J → N, prebacivanje režima i stvarno soft-delete brisanje jedne settle-up uplate.
3. Djelimično plaćanje i slanje zastarjelog većeg iznosa.
4. Ponovno slanje veze nakon potpunog poravnanja.
5. Korisnik u dvije grupe sa suprotnim saldima i različitim podešavanjima; usklađenost balances, overview, overall, My Groups, Recent Groups i `GetMemberNetInGroupAsync`.
6. EF command interceptor: očekivano pet SELECT upita za jednu i za tri grupe, bez rasta po grupi.

Ovi testovi su kompajlirani i otkriveni, ali nisu izvršeni. Postojeći API test neusklađenih podataka proširen je provjerom javne poruke bez internog teksta izuzetka, stack trace-a i identifikatora grupe u detalju greške. Provjera da neispravni podaci nisu automatski prepisani ostaje.

## Potvrđena i ispravljena greška

`BalanceService` je prosljeđivao identifikator grupe i `ex.Message` u javnu poruku `409 financial_data_inconsistent`. Time bi kontrolisana greška mogla izložiti interni detalj, naročito za `KeyNotFoundException`.

Sada se originalni izuzetak i identifikator grupe evidentiraju u serverskom logu, a javna poruka je samo `Financial data is inconsistent. Please contact support.` Status, kod greške, struktura ProblemDetails odgovora i API rute ostaju isti. Nije promijenjena stroga validacija, zapisivanje stvarnih uplata niti novčani podaci. Unit testovi nisu pokazali dodatni finansijski bug; potvrda javnog HTTP odgovora čeka integraciono izvršavanje.

## Semantika zastarjelih Settle Up zahtjeva

Postojeći contract prima članove i iznos stvarne uplate, bez identifikatora verzije grafa, očekivanog salda ili idempotency ključa. Zato ne može pouzdano razlikovati zastarjeli ekran, mrežni retry i drugu stvarnu uplatu. U skladu sa odobrenim planom, ova faza ne uvodi novo ograničenje niti tiho odbacuje novac.

Ako J prvo uplati B 10.00, preostalo B-ovo potraživanje je 16.33. Ako zatim po starom ekranu uplati još 26.33, B-ov neto postaje -10.00, a J-ov -3.67. To je stvarna preplata. Nakon potpunog poravnanja, dodatna uplata J → N: 9.33 stvara N-ov dug J-u 9.33; originalni izmireni neto saldi drugih članova ostaju nula. Izvršeni unit testovi potvrđuju ove salde.

U regularnom režimu mogu ostati neto-nulti posrednici na preostaloj stvarnoj obavezi. Direktnu neto vezu garantuje pojednostavljeni režim; prazan graf u oba režima garantuje se kada su svi saldi nula. Novu preplatu ne treba proglasiti ponovnim pojavljivanjem starog izmirenog duga.

Rizik prije produkcije: automatski ponovljen POST može evidentirati dodatnu uplatu. Zaštita od dupliranja ili poslovno odbijanje zastarjelih iznosa zahtijeva zasebnu odluku; nije dodata kao novi feature u ovoj fazi.

## Performanse i ograničenja dokaza

Statički pregled potvrđuje da `GetGroupsBalancesAsync` izvršava pet grupnih SELECT upita izvan petlje, pravi lookupe i poziva kalkulator jednom po grupi. U petlji po grupama nema SQL upita. Izvorne uplate i raspodjele učitavaju se jednom po batch-u, a veze se sabiraju po paru prije kalkulatora. Grupni snapshot sprečava kombinovanje podataka iz različitih trenutaka kod konkurentne izmjene.

Postoji konstantan trošak dodatnog čitanja metapodataka u pozivajućim servisima. Liste grupa sada obrađuju relevantnu istoriju svih grupa na učitanoj stranici, a overall endpoint svih aktivnih grupa korisnika. To daje konzistentne cente, ali CPU i memorija rastu sa istorijom. Nije uveden keš ili novi refactoring bez podataka o stvarnoj potrebi.

Test broja SQL upita čeka Docker. Nema dostupnog mjerenja starog pristupa i nije prijavljeno poređenje vremena, benchmark ili potvrda opterećenja na velikoj bazi.

## Preostalo prije produkcije

- Pokrenuti 61 relevantni API slučaj uz PostgreSQL Testcontainers; posebno potvrditi provider upite, snapshot, soft-delete i sanitizaciju 409 odgovora.
- Ručno provjeriti navedeni graf, puno/djelimično poravnanje i promjenu podešavanja na lokalnoj ili testnoj bazi.
- Procijeniti performanse na reprezentativnoj istoriji; trenutno nema baznog mjerenja.
- Razmotriti postojeće build upozorenje `NU1903` za `Microsoft.AspNetCore.WebUtilities` 8.0.20 u odvojenom zahvatu nad zavisnostima.
- Postojeće ograničenje na jedinstvenu valutu sa dvije decimale ostaje dokumentovano u ciljanoj analizi preciznosti.

Backend je spreman za ručno API testiranje na testnom okruženju. To nije potvrda produkcione spremnosti: PostgreSQL integraciona validacija još nije izvršena. Baza, migracije, originalni podaci, API contract i implementacioni plan nisu mijenjani; deployment nije rađen.
