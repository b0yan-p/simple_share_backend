# simplifyDebts: analiza i plan implementacije

Obuhvat: samo `simpleShare_backend`. Frontend, rute i struktura API odgovora ostaju isti. Ovo je dokument za pregled i potvrdu odluka; implementacija nije započeta.

Ova revizija zamjenjuje ranije preporuke da regularni obračun ostane potpuno nepromijenjen, da se greedy primjenjuje na cijelu grupu i da se zadrži nezavisan obračun `Members.Net`. Ta tri pravila nisu dovoljna da se ispune novi zahtjevi. Ispod su predložene najmanje korekcije i njihove posljedice; odluke koje traže potvrdu su na kraju dokumenta.

## Postojeća osnova i ograničen obuhvat analize

Provjereni su postojeći plan i relevantni dijelovi `Services/BalanceService.cs`, `Services/ExpenseService.cs`, `Services/GroupMemberService.cs`, balance modela i prethodno dokumentovane greške zaokruživanja. Kompletan projekat nije ponovo analiziran.

- `Group.SimplifyDebts`, kolona u bazi i postojeći zahtjevi/odgovori već postoje. Za preporučeno rješenje nije potrebna migracija.
- `BalanceService.GetGroupBalancesAsync` pravi proporcionalne veze `split.Amount * payment.Amount / totalPaid`, prebija ih po paru u `BuildNetByPair`, pa u `BuildNetDebts` odbacuje apsolutne iznose manje od `0.01` i zaokružuje ostale koristeći `AwayFromZero`.
- `Members.Paid` i `Members.Owed` trenutno se zaokružuju nezavisno; `MemberBalance.Net` je njihov izračunati zbir sa predznakom, odnosno `Paid - Owed`.
- `GroupService.GetGroupOverviewAsync` izvodi otvorena potraživanja i dugove iz vraćenih veza.
- `GetMyOverallBalanceAsync` trenutno učitava samo veze trenutnog člana. To nije dovoljno ni za uklanjanje ciklusa regularnog grafa ni za pojednostavljivanje.
- `GetMemberNetInGroupAsync`, koji se koristi pri uklanjanju člana, ima zaseban obračun i zaokruživanje neta.
- Ranije utvrđena korekcija ostaje u planu: ukloniti inicijalizator `false` sa nullable `CreateGroupRequest.SimplifyDebts`. Kreiranje već koristi `?? false`, a izmjena `?? dbGroup.SimplifyDebts`; izostavljeno polje tada neće isključiti opciju pri izmjeni naziva.

## Zajednički model obračuna

Preporuka je jedan mali kalkulator bez baze, npr. `DebtGraphCalculator`, sa zajedničkim iznosima u centima za oba režima:

```text
neobrisani troškovi i stvarne uplate
    → zajednički obračun u centima i regularnih veza
    → uklanjanje finansijski neutralnih ciklusa
    → regularni graf R
    → ako je SimplifyDebts = true: pojednostavljivanje komponenti R
    → jedan odabrani graf kroz postojeći API
```

`SimplifyDebts` bira raspored preostalih obaveza. Ne bira drugo pravilo za saldo ili zaokruživanje. Nikakav izračunati graf, raspodjela centi ili uklonjeni ciklus ne upisuje se kao novi trošak ili stvarna uplata.

## 1. Settle Up i promjena podešavanja

### Potvrđeno ponašanje postojećeg koda

`ExpenseService.SettleUpDebtAsync` za uplatu `A → C: 10` kreira postojeći tip troška sa `IsSettleUp = true`, uplatom člana A i raspodjelom na člana C. Ne provjerava da li je ranije postojao direktan dug između njih i ne čuva način prikaza u trenutku plaćanja.

Za obračun dugova taj zapis daje obrnutu vezu `C → A: 10`: A je svojim plaćanjem smanjio dug, a C svoje potraživanje. To je ispravno za neto pozicije. Međutim, uz postojeće `A → B: 10` i `B → C: 10` nastaje ciklus koji `BuildNetByPair` ne može ukloniti. Svi neto saldi su nula, ali regularni prikaz prikazuje tri otvorene veze. Prvi plan je predvidio ispravno knjiženje neta, ali nije riješio ovaj problem prikaza.

### Zašto potpuno očuvanje stare semantike nije moguće

Ako regularni graf mora doslovno prikazivati neto svakog pojedinačnog para iz originalnih zapisa, sve tri veze iz primjera moraju ostati. Ako mora istovremeno prikazati da su svi izmireni, te veze moraju nestati. Oba zahtjeva ne mogu važiti istovremeno.

Neto nula sam po sebi nije razlog da se iz grafa izbriše član: u početnom lancu B ima neto nula, ali njegove dvije veze su potrebne da se očuvaju A-ov dug i C-ovo potraživanje.

### Preporuka: uklanjanje ciklusa u oba režima

Regularni graf definisati kao direktne odnose iz ledgera, korigovane zajedničkim pravilom za cente iz tačke 3, nakon prebijanja parova i uklanjanja ciklusa. Zadržava posrednike i veze koje nisu dio ciklusa; ne rekonstruiše se globalno samo iz neto salda.

Postupak u centima:

1. Formirati usmjerene pozitivne veze nakon prebijanja parova.
2. Pronaći ciklus determinističkim DFS obilaskom: početni čvorovi i susjedi sortirani po `MemberId`.
3. Od svake veze u tom ciklusu oduzeti najmanji iznos u ciklusu.
4. Obrisati veze sa nulom i ponavljati dok nema ciklusa.

Svaki član ciklusa gubi isti iznos ulaznog i izlaznog duga, pa se njegov neto ne mijenja. Svaki korak uklanja bar jednu vezu, pa postupak završava. Za jednostavnu implementaciju sa ponovljenim DFS obilaskom gornja granica je `O(m * (n + m))`, uz `O(n + m)` memorije, gdje su n i m broj članova i veza. Za očekivane male grupe to je prihvatljivije od uvođenja zasebnog sistema knjiženja poravnanja.

Kod više preklopljenih ciklusa može postojati više ispravnih regularnih grafova. Fiksni redoslijed obezbjeđuje ponovljiv rezultat za iste zapise. Promjena troška ili nova uplata može promijeniti putanje preostalih dugova; samo promjena podešavanja ne mijenja regularni rezultat R.

### Očekivani rezultati potpunog i djelimičnog plaćanja

Sljedeći redovi opisuju istu početnu istoriju; djelimično plaćanje je zasebna grana od potpunog. Iznosi su u KM.

| Stanje istorije | Neto A / B / C | `false`: regularni R | `true`: pojednostavljeni |
| --- | --- | --- | --- |
| Početno stanje | -10 / 0 / +10 | A → B: 10; B → C: 10 | A → C: 10 |
| Samo uključivanje i ponovno isključivanje | -10 / 0 / +10 | A → B: 10; B → C: 10 | A → C: 10 |
| A stvarno plati C 10 | 0 / 0 / 0 | Prazan graf | Prazan graf |
| A umjesto toga plati C 4 | -6 / 0 / +6 | A → B: 6; B → C: 6 | A → C: 6 |
| Poslije tih 4, A plati C još 6 | 0 / 0 / 0 | Prazan graf | Prazan graf |
| Poslije tih 4, u regularnom režimu A plati B 3 | -3 / -3 / +6 | A → B: 3; B → C: 6 | A → C: 3; B → C: 3 |

Nakon svakog reda `true → false → true` daje odgovarajuće dvije kolone, uz iste neto salde i istu istoriju. Potpuno ili djelimično plaćanje nije posebna vrsta virtuelnog poravnanja: ostaje stvarna uplata koja već postoji u modelu.

Ako korisnik uplati više od otvorenog duga, obrnuti neto predstavlja stvarnu preplatu i ne smije se izbrisati kao ciklus. Ovaj plan ne uvodi novo ograničenje iznosa stvarne uplate. Ako se uplata kasnije obriše kroz postojeći tok brisanja troška, dugovi se ponovo računaju bez nje i mogu se legitimno otvoriti.

### Prednosti, nedostaci i alternative

- Prednosti: nema novih tabela ni zapisa poravnanja; radi i za postojeću istoriju, potpune i djelimične uplate; podešavanje se slobodno mijenja; ciklusi ne traže novo plaćanje.
- Nedostatak: regularni prikaz više nije doslovno isti kao stari graf po paru. Uklanjaju se i raniji neutralni ciklusi koji nisu nastali kroz pojednostavljeni settle-up. To je potrebna i vidljiva promjena poslovne semantike.
- Samo vraćanje praznog grafa kada cijela grupa ima neto nula nije dovoljno: djelimična plaćanja i neutralni ciklus pored drugog otvorenog duga ostaju problem.
- Alternativa sa eksplicitnom alokacijom stvarne uplate duž originalne putanje može sačuvati preciznije porijeklo poravnanja, ali traži metapodatke, pravila za ponovni obračun nakon izmjene/brisanja troška i razdvajanje novčanog toka od alokacije. Nije preporučena za ovaj feature.
- Zabrana isključivanja opcije nije potrebna.

### Konkretni testovi

- Svaki red gornje tabele, uz prebacivanje režima nakon svake uplate i provjeru sva tri salda.
- Neutralni ciklus A → B → C → A uz nezavisan dug D → E: ciklus nestaje, D → E ostaje u oba režima.
- Nejednaki ciklus A → B: 10, B → C: 7, C → A: 4: R je A → B: 6, B → C: 3; neto je -6 / +3 / +3.
- Dva preklopljena ciklusa, permutovan ulaz i ponovljena čitanja: isti R i bez preostalih ciklusa.
- Uplata A → C: 12 u početnom primjeru: A ima +2, C -2; obaveza C → A: 2 ostaje, bez skrivanja preplate.
- Brisanje settle-up uplate vraća odgovarajući dug; brisanje/izmjena originalnog troška preračunava oba režima iz preostale istorije.
- Promjena podešavanja ne kreira, mijenja niti briše expense/payment/split zapise; stvarni settle-up kreira samo postojeći zapis stvarne uplate.

## 2. Greedy i broj transakcija

### Potvrda problema i jednostavnija zaštita

Za A → B: 3, B → C: 7, D → E: 5 neto pozicije su A -3, B -4, C +7, D -5 i E +5. Greedy na cijeloj grupi bira D → C: 5, B → E: 4, A → C: 2, A → E: 1: četiri veze umjesto početne tri.

Najjednostavnija pouzdana preporuka je **greedy zasebno po slabo povezanim komponentama regularnog grafa R**, uz završnu zaštitu broja veza. Komponente se traže posmatrajući veze kao neusmjerene, poslije uklanjanja ciklusa. Ne uvoditi dug između nepovezanih dijelova R.

U primjeru komponente su {A, B, C} i {D, E}. Rezultat je B → C: 4, A → C: 3, D → E: 5, odnosno tri veze. Uklonjen je posrednik B u odnosu A–C, iako broj uplata nije strogo smanjen.

### Zašto broj veza ne može porasti

Za jednu povezanu komponentu sa n čvorova, m regularnih veza i k članova sa nenultim netom:

- povezanost znači `m >= n - 1`;
- greedy između neto dužnika i povjerilaca stvara najviše `k - 1` veza;
- pošto je `k <= n`, za neprazan rezultat važi `k - 1 <= n - 1 <= m`.

Komponente sa nultim netom svih članova već su uklonjene kroz uklanjanje ciklusa. Sabiranjem granica po komponentama dobijamo da broj pojednostavljenih veza ne može biti veći od broja veza u R. Ovo je jača i korisnija garancija od samog globalnog greedy pokušaja sa naknadnim odbacivanjem.

### Pravilo izbora i determinističnost

- Za svaku komponentu uparivati najveći dug i najveće potraživanje; prenijeti minimum i vratiti nenulti ostatak u prioritetni red.
- Kod jednakih iznosa koristiti `MemberId`. Članove, komponente i krajnji izlaz sortirati stabilno; ne zavisiti od redoslijeda SQL rezultata ili trenutnog korisnika.
- Prihvatiti kandidat ako ima manje ili jednako veza od R. Pri jednakom broju uklanjanje posrednika i dalje je korisno. Za dva člana rezultat obično ostaje isti.
- Defanzivno provjeriti i jednakost neto pozicija i `candidate.Count <= R.Count`. Ako bilo šta ne važi, vratiti ispravan R i evidentirati internu grešku. To nije očekivan redovan ishod algoritma, nego zaštita od implementacione greške.
- Ako je R prazan, vratiti prazan graf. Ne vraćati originalni, neočišćeni graf kao rezervu: time bi se vratili ciklusi iz prve tačke.

U redovnom toku pojednostavljeni graf nema posrednike: isti član nema i ulazne i izlazne obaveze. Finansijska konzistentnost ostaje očuvana jer se polazi od istog R i istih iznosa u centima; ne mijenja se pravilo zaokruživanja radi manje veza.

### Optimalni algoritam za manje grupe

Tačan pristup može maksimalno podijeliti nenulte salde na podskupove sa zbirom nula. Ako takvih disjunktnih podskupova ima p, minimalan broj uplata je `k - p`. Dinamičko programiranje koje enumeriše podskupove i njihove podskupove daje jednostavnu egzaktnu varijantu sa `O(3^k)` vremena i `O(2^k)` memorije, pa zatim rekonstrukcijom uplata unutar izabranih podskupova.

Za k = 10 riječ je o redu veličine 59 hiljada kombinacija, za k = 12 oko 531 hiljadu, a za k = 16 oko 43 miliona, prije konstantnih troškova. To su matematičke procjene pretrage, ne izmjerena vremena na ShareOrb serveru. Trebalo bi dodati prag, rekonstrukciju, pravila izjednačenja i greedy rezervu za veće grupe. Optimum bi bio unutar dozvoljenih komponenti; dopuštanje uplata između nepovezanih komponenti predstavlja dodatnu poslovnu odluku.

**Preporuka za ShareOrb:** komponentni greedy sa provjerom granice. Njegovo vrijeme je `O(n + m + k log k)`, uz linearnu memoriju, nakon formiranja R. Već garantuje da feature ne povećava broj uplata, bez eksponencijalne pretrage. Ne obećavati apsolutni minimum i ne uvoditi optimalni algoritam dok ne postoji konkretna potreba.

### Konkretni testovi

- Navedeni primjer: globalni greedy može dati 4, komponentni rezultat mora imati 3 i navedene iznose.
- Lanac A → B: 10, B → C: 10: 2 veze postaju 1.
- Već direktan graf sa dva člana: isti dug, isti broj veza.
- Povezana komponenta sa više dužnika i povjerilaca: broj veza najviše k - 1 i nikad više nego u njenom R.
- Više komponenti, izolovani članovi i prazna grupa: nema novih veza između komponenti.
- Jednaki saldi i permutovan ulaz: identičan rezultat, uključujući izbor primalaca.
- Generisani mali grafovi: kandidat čuva svaki saldo, nema ciklusa/samodugovanja/negativnih iznosa i ima najviše onoliko veza koliko R.
- Potpuno ili djelimično plaćanje kandidata, zatim isključivanje opcije: primijeniti pravila prve tačke; nikad rezervni povratak na neobrađeni graf.

## 3. Preciznost, zaokruživanje i saldi članova

### Problem u prvom planu i postojećem kodu

Da, prethodni prijedlog mogao bi dati pojednostavljeni graf čiji neto po članu odstupa od `Members.Net`. Odvojeno zaokruživanje istorijskih ukupnih uplata i raspodjela, zaokruživanje dugova po paru i grupna raspodjela ostataka predstavljaju različita pravila.

Primjer sa četiri decimale: A plati 10.0050, a B i C imaju po 5.0025 raspodjele. Trenutni `Members` prikazuje A +10.01, B -5.00 i C -5.00, čiji zbir iznosi +0.01. Graf ne može istovremeno očuvati sva tri takva neta i imati nulti zbir. Promjena samo `Net`, uz zadržavanje postojećeg gettera `Paid - Owed`, takođe nije moguća.

Nije dovoljno provjeriti da ukupan zbir iznosa dugova odgovara zbiru potraživanja: svaka usmjerena veza to već zadovoljava. Potrebna je i jednakost neta grafa i prikazanog salda za svakog člana.

### Preporuka: jedinstveni prikaz u centima za oba režima

Sačuvati četiri decimale u bazi i postojeće značenje `Paid`/`Owed` kao istorijskih ukupnih uplata/raspodjela, uključujući stvarni settle-up. Za API napraviti zajedničku, uravnoteženu projekciju tih ukupnih iznosa na cente. `Net` i dalje ostaje `Paid - Owed`; ne dodavati polja ni mijenjati tipove API odgovora.

Predloženi minimalni postupak unutar istog kalkulatora:

1. Iz svih relevantnih zapisa učitati nezaokružene ukupne uplate P i raspodjele O po članu i proporcionalne veze. Čuvati `decimal` do prelaska na cente; ne koristiti `double`.
2. Pronaći slabo povezane komponente izvornog grafa učesnika **prije prebijanja parova, odbacivanja malih veza i uklanjanja ciklusa**. Uključiti i samostalne učesnike sa samo sopstvenim troškom. Ove komponente služe samo raspodjeli centi i razlikuju se od komponenti R za greedy.
3. Za svaku takvu komponentu odrediti zajednički ukupan iznos T u centima za uplate i raspodjele: za validne, usklađene zapise `T = Round(sum(P), 2, AwayFromZero) = Round(sum(O), 2, AwayFromZero)`. Ako podaci nisu usklađeni, ne izmišljati finansijsku korekciju; to prijaviti kao grešku integriteta. Postojeća tolerancija upisa znači da ovaj slučaj treba eksplicitno testirati, a ne pretpostaviti da je nemoguć.
4. Uplate po članu pretvoriti u cente metodom najvećih ostataka do ukupnog T: uzeti donji cijeli broj centi svakog iznosa, zatim dodijeliti preostale cente po najvećem ostatku. Isto, zasebno, uraditi za raspodjele do istog T. Kod izjednačenja uvijek isti `MemberId`. Svaki iznos ostaje nenegativan i dobija donji ili gornji susjedni broj centi; odstupanje od izvorne agregirane vrijednosti je manje od jednog centa.
5. Postaviti API `Paid = P_cent / 100`, `Owed = O_cent / 100`, `N_cent = P_cent - O_cent`, `Side` iz N i sortiranje članova po ovom istom netu. Zbir N je nula u svakoj izvornoj komponenti i u grupi, a podešavanje ga ne mijenja.
6. Napraviti regularne veze iz prebijenih proporcionalnih parova u centima, zatim uskladiti njihove neto pozicije sa N korekcijom ostataka opisanom ispod. Tek tada uklanjati cikluse i birati regularni ili pojednostavljeni prikaz.

Komponente za raspodjelu centi koriste izvorne veze i učesnike čak i kad su im dugovi međusobno prebijeni. Time dodavanje stvarne uplate u centima unutar komponente ne mijenja njenu podjelu niti redoslijed decimalnih ostataka: platiocu se prikazani neto poveća, a primaocu smanji za tačan iznos uplate. Ponavljanje čitanja i prebacivanje podešavanja nema kumulativno zaokruživanje. Pri izmjeni ili brisanju izvornih troškova obračun se opravdano radi ponovo iz izmijenjenih podataka.

### Mala korekcija regularnih veza umjesto velikog refactoringa

Samo uravnoteženje `Paid` i `Owed` nije dovoljno: nezavisno zaokružene proporcionalne veze i dalje mogu imati drugačiji neto, čak i kada su svi originalni unosi u centima. Zato je potrebna jedna eksplicitna korekcija regularnog grafa:

- Potpisani iznos svakog para, u fiksnoj orijentaciji po `MemberId`, zaokružiti na najbliži cijeli cent. Za tačno pola centa koristiti fiksnu orijentaciju para, odnosno `floor(signedAmount * 100 + 0.5)`, a ne promjenljivo pravilo zavisno od smjera. To garantuje `Q(x + cijeli_centi) = Q(x) + cijeli_centi`, uključujući prelazak preko nule.
- Iz tih privremenih veza izračunati `graphNet[i]` i razliku `delta[i] = N_cent[i] - graphNet[i]`.
- Zbir delta je nula. Unutar svake izvorne komponente upariti negativne i pozitivne delta vrijednosti koristeći isti deterministički postupak uparivanja. Dodati nastale korekcije potpisanim parovima, ponovo prebiti suprotne veze i ukloniti nule.
- Ovo su samo korekcije zaokruživanja u izračunatom prikazu. Nisu stvarne uplate i ne čuvaju se u istoriji. Mogu povećati/smanjiti postojeću vezu ili stvoriti malu vezu između ranije indirektno povezanih članova iste komponente. Ukupna korekcija nije nužno samo jedan cent: može se akumulirati iz više zaokruženih parova.
- Nakon toga neto regularnog grafa mora tačno odgovarati N svakog člana. Ako ne odgovara, to je greška kalkulatora; ne vraćati uspješan finansijski nekonzistentan odgovor.
- Uklanjanje ciklusa iz tačke 1 i komponentni greedy iz tačke 2 čuvaju ovaj neto. Granica broja transakcija poredi pojednostavljeni rezultat sa ovako usklađenim regularnim R, a ne sa istorijskim brojem redova troškova ili starim nekonzistentnim prikazom.

Ovaj dodatni prolaz omogućava zadržavanje postojećeg proporcionalnog SQL obračuna, uz zajedničku projekciju iznosa i bez novih tabela, čuvanja alokacija ili mrežnog optimizacionog algoritma. Cijena je eksplicitna, mala promjena značenja regularnih veza na nivou zaokruživanja. Nije moguće obećati da svaka regularna veza ostaje identična staroj i istovremeno ispraviti postojeće neto razlike.

### Prag ispod jednog centa

Preporuka je ukinuti nezavisno odbacivanje nezaokruženih veza manjih od `0.01` u zajedničkoj projekciji. Nakon konačnog obračuna vraćaju se samo pozitivni cijeli centi; izvorni ostaci su već obuhvaćeni uravnoteženim zaokruživanjem.

Na primjer, dug od 0.009 KM može postati 0.01 KM, dok 0.004 KM u samostalnoj dvoročlanoj komponenti postaje 0.00 KM. To mijenja ranije ponašanje testa sa pragom po paru. Ako poslovno treba zadržati oprost ispod centa, on mora postati zajedničko pravilo i za istorijske prikazane salde, uz jasno dodjeljivanje ostataka. Ne primjenjivati ga samo na veze. Takav oprost nije preporučen kao dodatna politika u ovoj implementaciji.

### Primjeri očekivanog prikaza i testovi

U tabeli važi stabilni poredak identifikatora A < B < C. B i C nemaju sopstvene uplate; A nema sopstvenu raspodjelu. Iznosi su u KM.

| Izvorni zapis | API uplate A | API raspodjele | API neto A / B / C | Graf u oba režima za ove primjere |
| --- | --- | --- | --- | --- |
| A plati 0.010, B i C po 0.005 | 0.01 | B 0.01; C 0.00 | +0.01 / -0.01 / 0 | B → A: 0.01 |
| A plati 10.0050, B i C po 5.0025 | 10.01 | B 5.01; C 5.00 | +10.01 / -5.01 / -5.00 | B → A: 5.01; C → A: 5.00 |
| A plati 0.0150, B i C po 0.0075 | 0.02 | B 0.01; C 0.01 | +0.02 / -0.01 / -0.01 | B → A: 0.01; C → A: 0.01 |
| A plati 0.009, sve za B | 0.01 | B 0.01 | +0.01 / -0.01 / 0 | B → A: 0.01 |
| A plati 0.004, sve za B | 0.00 | B 0.00 | 0 / 0 / 0 | Prazan |

Dodatni obavezni testovi:

- Granica 0.005, 0.0049, 0.0051, pozitivni i negativni potpisani parovi i uplata cijelog centa preko nule. Tačno pola centa mora imati stabilno pravilo, bez naknadnog centnog duga.
- Više platiša sa iznosima u centima: A i B plate po 0.50; raspodjele C 0.33, D 0.33, E 0.34. Proporcionalne veze od 0.165 pokazuju da ni dvodecimalni unosi ne uklanjaju potrebu za zajedničkom korekcijom. Konačni neto mora biti A/B +0.50, C/D -0.33, E -0.34.
- Svaki primjer iz tabele potpuno izmiriti predloženim uplatama, pa mijenjati režim: svi N su nula i oba grafa su prazna, iako baza i dalje čuva izvorne decimalne ostatke.
- Djelimično poravnanje, uključujući 0.01 KM: saldo se mijenja tačno za uplatu; ne dolazi do nove raspodjele ostataka zbog samog prebacivanja režima.
- Za svakog člana, uključujući istorijskog učesnika sa preostalim saldom: `Paid - Owed = Net = incoming - outgoing`; zbir N je nula. Ove jednakosti moraju važiti zasebno za R i za pojednostavljeni graf.
- `sum(Paid) = sum(Owed)` nad svim relevantnim učesnicima. Zbir ulaznih iznosa grafa jednak je zbiru izlaznih iznosa grafa. Ne zahtijevati `sum(edge.Amount) = sum(Paid)`: sopstveni dio troška, posrednici, prebijanje i ciklusi to čine pogrešnom invarijantom.
- Permutacije učesnika i SQL redova, drugi trenutni korisnik, višestruka čitanja: ista raspodjela centi i isti graf.
- Samo sopstveni trošak, prazna komponenta, istorijski članovi i više nepovezanih komponenti: bez centnih transfera između nepovezanih komponenti.
- Neusklađeni izvorni zbir uplata i raspodjela: eksplicitna provjera integriteta, uključujući slučaj kada se zbirna razlika pojavi iz više dozvoljenih tolerancija upisa. Ne dodjeljivati proizvoljno razliku članu.

### Prednosti, nedostaci i razmotrene alternative

- Prednosti: postojeći contract i `Net = Paid - Owed` ostaju; oba režima koriste isti novac; prikaz je deterministički, zbirno uravnotežen i izmiriv u centima; baza i istorija ostaju netaknute.
- Nedostaci: neki `Paid`/`Owed` iznosi mogu biti za cent drugačiji od dosadašnjeg nezavisnog zaokruživanja; prag po paru se mijenja; regularne veze mogu dobiti korekcije ostataka. To zahtijeva potvrdu i ciljane promjene postojećih očekivanja testova.
- Zadržati istorijske `Paid`/`Owed`, a `Net` računati samo iz grafa, jeste manje koda, ali ostavlja vidljivo `Net != Paid - Owed`. Nije preporučeno jer ne rješava traženu konzistentnost odgovora.
- Izjednačiti `Paid`/`Owed` sa ulaznim/izlaznim otvorenim dugovima promijenilo bi njihovo značenje iz istorijskih iznosa u trenutno otvorene obaveze. Nije preporučeno.
- Zaokruživanje svake pojedinačne uplate i raspodjele pri upisu ili migracija istorije narušava zadato očuvanje izvornog novca. Zabrana novih unosa sa više od dvije decimale sama ne rješava stare podatke ni proporcionalne iznose poput 0.165. Zato nije rješenje ovog plana.

## Povezivanje sa postojećim servisima

1. **Podešavanje grupe:** zadržati postojeći model i API; popraviti samo očuvanje nullable podešavanja pri izmjeni naziva.
2. **Zajednički kalkulator:** grupno učitati nezaokružene P/O i proporcionalne veze za sve relevantne učesnike, sa imenima istorijskih učesnika. Unutar jednog kalkulatora uraditi projekciju u cente, korekciju regularnih veza, uklanjanje ciklusa i opcioni komponentni greedy. Nisu potrebni strategy registri, posebni servisi knjiženja ni keširanje izvedenog grafa.
3. **Grupni balance:** `Members`, njihov `Side` i izabrani `Debts` nastaju iz istih kanonskih iznosa. Istorijski učesnik sa otvorenom obavezom mora biti uključen u obračun i prikazane salde; ne smije se izgubiti filtriranjem samo aktivnih članova. Ne mijenjati time njegova prava pristupa.
4. **Overview:** `PaidTotal` i `OwedTotal` i dalje znače otvorena potraživanja i dugove trenutnog člana, izvedena iz izabranog grafa. Njihova razlika mora odgovarati njegovom `Members.Net`; njihovi pojedinačni iznosi mogu se razlikovati između režima zbog uklanjanja posrednika.
5. **Zbirni `/api/Balance/me`:** učitati kompletne grafove i osnovu za cente za oba režima, ne samo veze trenutnog člana. Grupisati obračun po grupi, koristiti isti kalkulator i tek onda izdvojiti korisnikove veze. Sabirati ulazne i izlazne veze eksplicitnim provjerama oba identifikatora; tuđe veze se ne sabiraju. Ne prebijati različite grupe i ne uvoditi poziv baze po grupi.
6. **GroupCount:** brojati grupe u kojima trenutni član ima bar jednu vezu u izabranom konačnom grafu. Neutralni ciklus više ne povećava broj ni u regularnom režimu. Posrednik sa netom nula može imati regularne veze, a u pojednostavljenom režimu nijednu; tada se njegov GroupCount opravdano razlikuje.
7. **Ostali prikazani neto saldi i provjera uklanjanja člana:** `GetMyGroupsAsync`, `GetRecentGroupsAsync` i `GetMemberNetInGroupAsync` moraju koristiti istu projekciju neta u centima, ne svoje nezavisno zaokruživanje. Ne moraju birati niti prikazivati pojednostavljene veze. Samo preslikavanje postojećeg `paid - owed` SQL zbira nije dovoljno za slučajeve sa ostacima; potrebne agregate učitati grupno i ponovo koristiti istu funkciju projekcije.
8. **Stvarni settle-up:** zadržati postojeći format zapisa i rutu. Iznos stvarne uplate se i dalje normalizuje na cente; slanje ili čitanje zahtjeva ne smije knjižiti virtuelne korekcije ili cikluse kao nove uplate.

## Validacija i redoslijed implementacije nakon potvrde

1. Potvrditi poslovne odluke na kraju dokumenta i zaključati navedene primjere kao specifikaciju.
2. Dodati testove postojeće greške sa resetovanjem podešavanja, ciklusom poslije settle-up i neusaglašenim prikazanim centima.
3. U malom unit test projektu provjeriti zajedničku projekciju centi, korekciju neta regularnih veza, uklanjanje ciklusa i komponentni greedy. Brojčani primjeri i generisani mali grafovi provjeravaju finansijske invarijante, ne detalje implementacije.
4. Povezati isti kalkulator sa grupnim i zbirnim endpointima i ponovo koristiti projekciju za ostale neto salde i uslov uklanjanja člana.
5. API testovima sa postojećim PostgreSQL Testcontainers okruženjem pokriti promjene podešavanja prije/poslije pune i djelimične uplate, mješavinu grupa sa obje opcije, sve centne primjere i očuvanje originalnih zapisa.
6. Pokrenuti `dotnet build`, unit testove i `dotnet test` za solution uz dostupan Docker. Ne očekivati da testovi koji doslovno zahtijevaju stari prag ispod centa ili prikaz neutralnih ciklusa ostanu isti: njihove promjene moraju pratiti potvrđenu specifikaciju, uz zadržavanje ostalih regresionih provjera.

Prihvatanje: jedan graf kroz postojeći contract; neto pozicije iste u oba režima; saldo svakog člana jednak netu njegovih veza i `Paid - Owed`; bez otvorenih ciklusa nakon finansijskog poravnanja; broj pojednostavljenih veza najviše broj regularnih veza; group/overview/overall odgovori usklađeni; bez izmjene istorije pri promjeni podešavanja ili čitanju.

U ovoj fazi izmijenjen je samo ovaj dokument. Produkcioni kod i testovi nisu mijenjani; implementacioni testovi nisu pokretani. Aritmetički su provjereni prikazani iznosi u centima i neto pozicije iz ključnih primjera; to nije zamjena za buduće unit i API testove.

## Otvorene odluke prije implementacije

1. **Semantika regularnog grafa — potrebna potvrda.** Preporuka: regularni graf po paru, nakon zajedničke korekcije centi i uklanjanja svih neutralnih ciklusa, u oba režima. To mijenja dosadašnje prikazivanje ciklusa čak i za grupe koje nikada nisu koristile Simplify Debts. Potpuna kompatibilnost sa starim grafom i traženo ponašanje nakon poravnanja nisu istovremeno mogući.
2. **Jedinstveno pravilo za cente — potrebna potvrda.** Preporuka: najveći ostaci za prikazane istorijske P/O iznose do istog ukupnog iznosa po izvornoj komponenti, stabilni `MemberId` za izjednačenja, korekcija regularnih veza do istog neta i ukidanje zasebnog praga po paru. Time neki ranije skriveni iznosi od 0.005–0.009 KM postaju jedan cent, a neki istorijski prikazani P/O iznosi pomjeraju se za cent. Potvrditi i prihvatljivost izvedenih centnih veza unutar izvorne komponente; bez tog kompromisa treba složenije kontrolisano zaokruživanje na postojećim vezama ili promjena API semantike.
3. **Cilj pojednostavljivanja — potrebna potvrda.** Preporuka: komponentni greedy, bez povezivanja nepovezanih komponenti R, uz garanciju da broj veza ne raste. Prihvatiti rezultat i kada je broj jednak, jer se mogu ukloniti posrednici. Apsolutni minimum transakcija nije obećanje feature-a. Egzaktni algoritam za male grupe odložiti.
4. **Neusklađena istorija uplata i raspodjela — potrebna potvrda načina postupanja.** Preporuka: ne popravljati novac automatski i ne skrivati razliku raspodjelom na proizvoljnog člana; vratiti kontrolisanu grešku integriteta ako zajednički ukupan iznos u centima ne može važiti za oba zbira. Prije implementacije odrediti dozvoljenu provjeru sirove tolerancije i način sanacije eventualnih već postojećih zapisa, ako se takvi podaci pronađu.

Implementacija ostaje na čekanju dok ove odluke i ažurirani plan ne budu pregledani.
