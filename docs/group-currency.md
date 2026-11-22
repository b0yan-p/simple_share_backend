# Group currency and expense snapshots

## Financial boundary

`Group.Currency` is the only calculation currency. `Expense.TotalAmount`, payment `Amount` and split `Amount` are normalized. Original total/currency and child originals are input/display/edit data. No read performs an FX request.

New financial requests use `originalAmount`, `originalCurrency`, original payment/split amounts, and `expenseDate: YYYY-MM-DD`. Updates supply an optional complete `financial` block; omit it for metadata edits. The entity date remains a timestamp, with new dates encoded at UTC midnight as a date label, without timezone conversion. Existing timestamps and four-decimal amounts are preserved by migration and metadata edits.

Amount/allocation-only edits reuse the stored rate/date. Currency/calendar-date changes fetch a new historical observation when foreign. Identity expenses and settle-up use rate 1 locally. New amounts use two decimals; the original expense input maximum is 100,000; converted totals may exceed it. Largest-remainder allocation preserves exact payment/split sums; any resulting zero child causes `400 normalized_allocation_too_small`.

Owner-only currency changes and all expense/settlement creation acquire `FOR UPDATE` on the group row. Any expense, including deleted/settlement rows, permanently locks currency. Overall balance responses contain currency buckets, never cross-currency scalar totals.

## Retrying ordinary expense creates

The client generates one UUID per new draft and submits it as `clientExpenseId` on every POST. The offline queue uses that same UUID as its IndexedDB key. The UUID becomes the globally unique expense primary key. After checking active membership and locking the group, the API checks the original creator and a stored SHA-256 fingerprint of the original create fields. A matching replay returns the saved expense without another financial write or FX lookup, including after an edit or soft deletion. A conflicting use returns HTTP 409 with code `expense_create_conflict`. Settlements continue to use their separate endpoint.

## Static catalog

The checked-in catalog is the intersection of Frankfurter's active fiat data and SIX's current ISO currency list, excluding funds/accounting/noncurrency units. It is application data, not a runtime provider dependency. Keep `Common/Currencies/fiat-currencies.json` and the mobile `src/assets/data/fiat-currencies.json` content identical when updating versioned entries.

Sources used for this catalog:

- https://api.frankfurter.dev/v2/currencies
- https://www.six-group.com/dam/download/financial-information/data-center/iso-currrency/lists/list-one.xml

## Public provider verification (2026-11-16 UTC)

Read-only public requests were checked against v2 single-pair and historical range endpoints:

| Pair/requested date | Single-pair or dated history snapshot | Rate |
|---|---|---|
| USD/BAM, 2026-09-10 (business day) | 2026-09-10 | 1.6810 |
| USD/BAM, 2026-09-12 (Saturday) | 2026-09-12 | 1.6846 |
| USD/BAM, 2026-09-13 (Sunday) | 2026-09-13 | 1.6848 |
| EUR/BAM, 2026-09-11 through 13 | A dated row exists for each day | 1.9558 |
| USD/BAM, 2026-11-16 | 2026-11-16 | 1.7715 |
| USD/BAM, 2026-11-17 before that day's snapshot is available | 2026-11-16 | 1.7715 |

The last probe requested a calendar date after available observations at the actual check time; the historical range also returned only November 16. It verifies real carry-forward to the preceding snapshot rather than relabelling it November 17. It is not a claim that a request was observed before publication earlier on November 16 itself.

Default blended snapshots can exist on weekends: do not force Friday merely because a request is Saturday/Sunday. For the checked pairs, dated range output agrees with the single-pair snapshots. `ExchangeRateDate` is the returned effective Frankfurter pair snapshot date, not a claim that all contributing central-bank observations share that date. The adapter validates direction, positive rate and effective date <= requested date; does not invent a date or use a latest-rate fallback.

The configured typed HttpClient uses `ExchangeRates:BaseUrl` (default public Frankfurter) and a 10-second timeout. Unsupported inputs return 400; absent historical observations return 400; outages/timeouts return 503; invalid upstream data returns 502. API tests replace the provider with a deterministic fake.

## Migration and local verification

`AddGroupCurrencyAndExpenseSnapshots` expands nullable columns, backfills BAM/identity snapshots and original allocations, then makes required fields non-null. It does not rewrite existing totals, children, timestamps or recalculate balances. No FX request occurs during backfill. No permanent financial defaults are added. No migration was applied to an existing application database during development; integration tests migrate isolated PostgreSQL databases.

The migration test inserts a representative deleted settlement at `2026-09-13T17:23:42Z`, amount `10.0051`, in the prior schema and verifies exact preservation after backfill. Real pre-feature database rows were not inspected; no production currency or timestamp inference was made.

Run:

    dotnet test tests/SimpleShare.UnitTests/SimpleShare.UnitTests.csproj
    dotnet test tests/SimpleShare.IntegrationTests/SimpleShare.IntegrationTests.csproj

Integration tests require Docker and use disposable PostgreSQL Testcontainers. No production writes are required.

Mobile checks:

    npm test -- --watch=false --browsers=ChromeHeadless
    npm run build
    npm run check:currency-catalog

IndexedDB v4 clears old pending requests and currency-incompatible cached expenses/groups/overviews as approved. Group-member cache remains. New pending requests hold only original inputs and selected date; current server group currency is authoritative at sync. There is no old-client financial compatibility or version gate.
