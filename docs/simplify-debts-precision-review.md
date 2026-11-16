# Ciljana provjera nakon Faze 2

## Nulte veze

`RawDebt` sa iznosom nula između dvije pozitivno nepovezane komponente izazivao je `KeyNotFoundException` pri dodavanju kreditorovog neta u rječnik dužnikove komponente. Kalkulator sada prvo validira sve iznose i identifikatore, a zatim jednom filtrira nulte veze za sve naredne korake. Nulte veze ne spajaju komponente; nulta veza prema nepoznatom članu i dalje predstavlja neispravan ulaz.

## Više platiša i decimalne trećine

Za uplate A=1 i B=2, uz raspodjele C=D=E=1, direktna formula `split * payment / total` daje konačne decimalne aproksimacije 1/3 i 2/3. Tri aproksimacije 1/3 nisu tačno 1 u tipu `decimal`. Prosljeđivanje takvih veza uz tačne izvorne `Member` ukupne iznose opravdano ne prolazi strogi ulazni ugovor kalkulatora, iako su izvorni troškovi validni.

Faza 2 već koristi `BalanceService.BuildExpenseLedger` umjesto direktnog prosljeđivanja te formule. Adapter prvo zahtijeva tačno slaganje izvornog ukupnog iznosa, uplata i raspodjela. Zatim deterministički raspoređuje ostatke proporcionalnog dijeljenja, uz očuvanje svakog reda (raspodjele) i kolone (uplate). Članovi kalkulatora nastaju iz izvornih P/O ukupnih iznosa, a pozitivne veze se sabiraju po paru. Izvorni podaci se ne mijenjaju.

Za ovaj primjer adapter daje tačne neto pozicije A=+100, B=+200 i C/D/E=-100 centi u oba izlazna grafa. Nije potrebna nova tolerancija niti promjena algoritma ili provjere integriteta. Izvorna razlika od 0.0001 KM se i dalje odbija prije raspodjele, čak i ako bi se oba zbira prikazala kao 3.00 KM. Servis tu grešku mapira na `409 financial_data_inconsistent`.

Regresioni testovi razlikuju reprodukciju problema direktne formule, uspješno pripremljene ulaze postojećeg adaptera i stvarno neusklađene podatke. API test pokriva isti trošak i kontrolisanu grešku u oba režima. Zbir iznosa veza regularnog grafa nije nužno jednak zbiru pozitivnih neto salda: centna korekcija može ostaviti posrednika. Mjerodavna provjera je neto svakog člana i nulti zbir svih salda.

## Ograničenje valuta

Trenutna konfiguracija mobilnog klijenta u `simpleShareMobile/src/app/features/expenses/utils/expense.constants.ts` eksplicitno koristi samo `BAM`, sa dvije decimale. Relevantni backend modeli `Group` i `Expense` i zahtjevi/odgovori nemaju identifikator valute. Backend zato ne može automatski izabrati broj minornih jedinica niti provjeriti valutu ili izvršiti konverziju. Nije utvrđena podrška za druge valute u ovom toku.

Kalkulator fiksno pretvara iznose množenjem/dijeljenjem sa 100; vrijednost `Cents` uvijek znači stoti dio jedne iste obračunske valute. Četiri decimalna mjesta u bazi predstavljaju preciznost pohranjenog iznosa, a ne podršku za valutu sa četiri minorne decimale. Jedna grupa i zbirni endpoint moraju se tumačiti u istoj valuti; sabiranje različitih valuta nije podržano.

Pretpostavka da sve valute koriste dvije decimale nije opšte tačna: BAM ima 2, JPY 0, a KWD i BHD 3. Primjeri su provjereni u [Adyen dokumentaciji o minorim jedinicama valuta](https://docs.adyen.com/development-resources/currency-codes). Za te primjere nema navedenih odstupanja od ISO pravila. Postoje i posebne procesorske konvencije za druge valute; ne treba ih automatski pretvarati u poslovna pravila ShareOrb-a.

Trenutni obračun je primjeren postojećem BAM toku, a nije opšti multi-currency kalkulator. Prosljeđivanje KWD 1.234 svodi ga na projekciju sa dvije decimale i gubi valutno značajnu preciznost; JPY bi dozvolio nedozvoljene frakcije od 0.01. Prije eventualne podrške tim valutama potrebni su identitet valute, njen broj minornih jedinica i pravila zbirnog prikaza. Ova provjera ne mijenja valutni model, API, bazu ni prethodni implementacioni plan i ne započinje Fazu 3.
