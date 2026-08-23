# API Integration Test Cases

Catalogue of integration test cases for the SimpleShare API
(`ghcr.io/b0yan-p/simple_share_backend`).

Strategy, tooling and CI wiring live in [api-testing.md](https://github.com/NikolaCokic/simpleshare-deployment/blob/main/docs/api-testing.md) - this document is only
*what* to test. It is a living checklist: when an endpoint is added, changed or removed, the
matching rows here change with it.

Case IDs (`AUTH-03`, `EXP-11`, ...) are stable. Reference them in commits, PRs and test method names
so a failing test maps back to a documented intent.

**Status (2026-08-23):** P0 implemented - 34 tests, 31 passing, 3 failing by design. P1, P2 and P3
not yet written.

---

## Conventions

These apply to every case below.

- **Black box over HTTP.** Tests drive the API through `HttpClient`, not by calling services
  directly. If a case cannot be expressed as a request and a response, it belongs in a unit test.
- **Assert the `code`, not just the status.** Every error goes through `GlobalExceptionHandler` and
  comes back as `application/problem+json` with a `code` extension (`splits_sum_invalid`,
  `member_not_in_group`, ...). That string is what the mobile app branches on, so it is the actual
  contract. A test that only checks `400` will pass straight through a rename that breaks the client.
- **Unique data per test.** Register a fresh user with a GUID-based email in each test rather than
  resetting the database between tests. Cheaper, and it removes ordering dependencies.
- **Real Postgres.** Every case assumes `postgres:16-alpine` in a container, with migrations applied
  by the app itself at startup - the same path production takes.
- **Auth via the real flow.** Get tokens from `POST /api/Auth/register`, never by minting them in the
  test. That keeps `TokenService` and the JWT validation config under test for free.

---

## Endpoint inventory

Current surface, and where each route is covered. Keep this table in sync first when routes change -
empty cells are the backlog.

| Method | Route | Auth | Cases |
|---|---|---|---|
| GET | `/health` | anonymous | `HLTH-01` |
| POST | `/api/Auth/register` | anonymous | `AUTH-01..04` |
| POST | `/api/Auth/login` | anonymous | `AUTH-05..07` |
| POST | `/api/Group` | required | `GRP-01`, `GRP-09` |
| PUT | `/api/Group` | required | `GRP-02`, `XT-01` |
| GET | `/api/Group` | required | `GRP-03` |
| GET | `/api/Group/recent` | required | `GRP-04`, `EDGE-02` |
| GET | `/api/Group/{groupId}` | required | `GRP-05`, `XT-01` |
| DELETE | `/api/Group/{groupId}` | required | `GRP-06`, `XT-01` |
| POST | `/api/Group/{groupId}/restore` | required | `GRP-07` |
| GET | `/api/Group/{groupId}/balances` | required | `BAL-01..05`, `XT-01` |
| GET | `/api/Group/{groupId}/overview` | required | `GRP-08`, `XT-01` |
| POST | `/api/GroupMember/{groupId}/members/bulk` | **missing** | `AUTH-08`, `MEM-01..03` |
| DELETE | `/api/GroupMember/{groupId}/members/{memberId}` | **missing** | `AUTH-08`, `MEM-04` |
| GET | `/api/GroupMember/{groupId}/members` | **missing** | `AUTH-08`, `MEM-05` |
| POST | `/api/Expense/{groupId}` | required | `EXP-01..10` |
| GET | `/api/Expense/{groupId}` | required | `EXP-11`, `XT-01` |
| GET | `/api/Expense/{groupId}/{expenseId}` | required | `EXP-12`, `XT-01` |
| PUT | `/api/Expense/{groupId}` | required | `EXP-13` |
| DELETE | `/api/Expense/{groupId}/{expenseId}` | required | `EXP-14`, `XT-01` |
| POST | `/api/Expense/{groupId}/settleUp` | required | `SET-01..05` |
| GET | `/api/UserConnection` | required | `CON-01` |
| GET | `/api/Activity/me` | required | `ACT-01..03` |

> **`GroupMemberController` carries no `[Authorize]` attribute.** All three of its routes answer
> anonymously today - confirmed by `AUTH-08`, which returns `404`, `403` and `415` instead of `401`.
> One of them leaks member data. Measured behaviour is in
> [cases expected to fail](#auth-08---confirmed-failing-3-of-20-routes).

---

## P0 - contract and auth

The layer the mobile app cannot work without. **Implemented** - 34 tests, 31 passing, 3 failing by
design (2026-08-23). Status column: `pass` verified green, `FAILS` red on purpose, see
[the section below](#cases-expected-to-fail-on-the-first-run).

| ID | Case | Expected | Status |
|---|---|---|---|
| `HLTH-01` | `GET /health` against an empty database | `200`. Proves migrations, the `Roles` `HasData` seed and startup seeding all survive a cold start | pass |
| `AUTH-01` | Register with a valid payload | `200`, body carries `Id`, `Email`, `FirstName`, `LastName` and a non-empty `Token` | pass |
| `AUTH-02` | Register with an email that already exists | `400` | pass |
| `AUTH-03` | Register with a password below policy (no digit / no uppercase / under 6 chars) | `400`, one case per rule | pass (4 cases) |
| `AUTH-04` | Register with `Password != RepeatPassword` | `400` | pass |
| `AUTH-05` | Login with correct credentials | `200` + token | pass |
| `AUTH-06` | Login with a wrong password | `401` | pass |
| `AUTH-07` | Login with an unknown email | `401`, **and a response body identical to `AUTH-06`** - no user enumeration | pass |
| `AUTH-08` | Table-driven: call every authenticated route with no `Authorization` header | `401` for all | **FAILS** on 3 of 20 routes |
| `AUTH-09` | Call an authenticated route with a token whose signature was altered | `401` | pass |
| `AUTH-10` | Call an authenticated route with an expired token | `401`. `ClockSkew` is 2 minutes, so backdate well past it | pass |
| `AUTH-11` | The token issued by register is accepted by an authenticated route | `200`. Guards the issuer / audience / signing-key wiring | pass |

Lives in `AuthTests.cs`, `HealthTests.cs` and `AuthorizationMatrixTests.cs`. `AUTH-10` is the one
sanctioned place a token is minted rather than obtained from `/api/Auth/register`: `TokenService`
issues 7-day tokens, so expiry cannot be reached any other way.

---

## P1 - core domain

| ID | Case | Expected |
|---|---|---|
| `GRP-01` | Create group | `200`, group returned with an id |
| `GRP-02` | Update group name via `PUT /api/Group` | new name visible on read-back |
| `GRP-03` | `GET /api/Group` lists only the caller's groups, paged | another user's groups absent; page size and total respected |
| `GRP-04` | `GET /api/Group/recent?take=2` | at most 2, most recent first |
| `GRP-05` | `GET /api/Group/{id}` for own group | `200` |
| `GRP-06` | Delete group (soft) | gone from `GET /api/Group` |
| `GRP-07` | Restore a deleted group | back in `GET /api/Group`, expenses intact |
| `GRP-08` | `GET /api/Group/{id}/overview` after two expenses | totals match the expenses created |
| `GRP-09` | Create group with a name over `MaxLength(48)` | `400`, `code = validation_error` |
| `MEM-01` | `members/bulk` adding an existing member | member appears in `GET .../members` as `Active` |
| `MEM-02` | `members/bulk` creating a virtual member (`DisplayName`, no user) | created, no user account |
| `MEM-03` | `members/bulk` with `InviteUser = true` and no `Email` | `400` |
| `MEM-04` | A non-owner tries to remove a member | `403` |
| `MEM-05` | A removed member moves to `Deleted` and stops appearing in active lists | list reflects removal |
| `EXP-01` | Create expense - single payer, explicit splits, happy path | `200`, `PaidBy` and `SplittedBy` match the request |
| `EXP-02` | Create expense with empty `Payments` | `400`, `code = payments_required` |
| `EXP-03` | Payment amount `<= 0` | `400`, `code = payment_amount_invalid` |
| `EXP-04` | Same member twice in `Payments` | `400`, `code = payments_not_unique` |
| `EXP-05` | `sum(Payments) != TotalAmount` | `400`, `code = invalid_request_data` |
| `EXP-06` | Empty `Splits` | `400`, `code = splits_required` |
| `EXP-07` | Split amount `<= 0` | `400`, `code = split_amount_invalid` |
| `EXP-08` | Same member twice in `Splits` | `400`, `code = splits_not_unique` |
| `EXP-09` | `sum(Splits) != TotalAmount` | `400`, `code = splits_sum_invalid` |
| `EXP-10` | A payer or split member who is not in the group | `403`, `code = member_not_in_group` |
| `EXP-11` | `GET /api/Expense/{groupId}` paging and ordering | newest first, per-row `Net` correct for the caller |
| `EXP-12` | `GET /api/Expense/{groupId}/{expenseId}` | full `ExpenseResponse` with `CreatedInfo` |
| `EXP-13` | Update an expense's amount | balances recalculated, `ModifiedInfo` populated |
| `EXP-14` | Delete an expense | balances return to their pre-expense values |
| `EXP-15` | Multi-payer: A pays 20, B pays 10, split equally three ways | owed amounts allocated proportionally to what each payer put in |
| `BAL-01` | A pays 30, split equally across A/B/C | B owes A 10, C owes A 10 |
| `BAL-02` | **Every member's net in a group sums to exactly `0`** after any sequence of expenses | invariant - assert it on every balance test |
| `BAL-03` | **10.00 split three ways** | nets still sum to `0`, no cent created or lost. Expected to fail on first run, see below |
| `BAL-04` | Two expenses in opposite directions net off | one netted pair, not two rows |
| `BAL-05` | A balance under the `0.01` settle threshold | reported as `Settled`, not as a one-cent debt |
| `SET-01` | Settle up in full | pair drops to `Settled` |
| `SET-02` | Settle up partially | remainder correct |
| `SET-03` | Settle up to yourself | `400`, `code = invalid_request` |
| `SET-04` | Settle up to a member outside the group | `403`, `code = member_not_in_group` |
| `SET-05` | Settle-up appears in `GET /api/Expense/{groupId}` with `IsSettleUp = true` | flag set |

---

## P2 - cross-tenant access

The API is publicly reachable through the Cloudflare tunnel. These cases decide whether a stranger
with a valid account can read someone else's data.

| ID | Case | Expected |
|---|---|---|
| `XT-01` | Table-driven: user B, authenticated, calls every `{groupId}` route against user A's group | `403` or `404` - never `200`, never `500` |
| `XT-02` | User B updates or deletes an expense in user A's group | `403` |
| `XT-03` | User B settles up inside user A's group | `403` |
| `XT-04` | User B adds themselves to user A's group via `members/bulk` | `403` |
| `EDGE-01` | Any `{groupId}` / `{expenseId}` route with a well-formed but non-existent GUID | `404` with a `code`, not `500` |
| `EDGE-02` | `/recent?take=0`, `take=-1`, `take=1000` | defined behaviour, no `500`, no unbounded query |
| `EDGE-03` | Malformed GUID in a route (`/api/Group/not-a-guid`) | `400` |
| `ACT-01` | `GET /api/Activity/me` returns only the caller's activity | user B's actions absent |
| `ACT-02` | Activity ordering is newest first, paging respected | correct |
| `ACT-03` | Create / update / delete an expense writes `ExpenseCreated` / `ExpenseUpdated` / `ExpenseDeleted` | one row each, correct `ActivityEntityType` |
| `CON-01` | `GET /api/UserConnection` lists only members the caller shares a group with | no global member list leak |

---

## P3 - robustness and shape

| ID | Case | Expected |
|---|---|---|
| `ROB-01` | Two expenses created concurrently on the same group | both persisted, balances consistent, no deadlock |
| `ROB-02` | Expense `Description` over `MaxLength(200)` | `400`, `code = validation_error` |
| `ROB-03` | A required field missing from any request body | `400`, `code = validation_error`, `Content-Type: application/problem+json` |
| `ROB-04` | Every error response carries both `code` and `traceId` extensions | assert once per error family |
| `ROB-05` | Boot the app twice against the same database | migrations and startup seeding are idempotent, second boot healthy |
| `ROB-06` | `TotalAmount` with more than 2 decimal places | defined behaviour - rejected or rounded, never silently drifting |

---

## Cases expected to fail on the first run

Not defects in the tests. They assert the *intended* behaviour and fail loudly until the API has it.
Do not `Skip` or delete them to get a green run.

### `AUTH-08` - confirmed failing, 3 of 20 routes

`GroupMemberController` carries no `[Authorize]` attribute. Measured, not predicted - each route
refuses for a different accidental reason, and none of them checks identity:

| Route | Expected | Actual | Why |
|---|---|---|---|
| `GET /api/GroupMember/{groupId}/members` | `401` | `404` | Reached the handler and queried the database. `404` only because the group ID does not exist - **with a real group ID this returns member names, user IDs, roles and join dates to an anonymous caller over the public tunnel** |
| `DELETE /api/GroupMember/{groupId}/members/{memberId}` | `401` | `403` | Handler ran, `User.FindFirstValue` found no claim, fell through to its own `Forbid()`. Refuses, but by accident |
| `POST /api/GroupMember/{groupId}/members/bulk` | `401` | `415` | Rejected at content-type negotiation before authorization was considered at all. The matrix sends no body; a real JSON body would get past this into `User.GetUserId()`, which throws `UnauthorizedAccessException` and surfaces as `500` |

The first row is a live data leak. Adding `[Authorize]` to the controller should turn all three into
clean `401`s with no change to the tests.

### Still expected to fail, not yet written

| ID | Why |
|---|---|
| `BAL-03` | Multi-payer allocation divides, then applies `Math.Round(..., 2, MidpointRounding.AwayFromZero)` per pair. Rounding after a proportional split is where a cent typically appears or vanishes |

### Resolved

| ID | Outcome |
|---|---|
| `HLTH-01` | **Passes.** Startup seeding is safe on an empty database - migrations, the `Roles` `HasData` seed and `DbSeeder` all complete on a fresh container. That the seeder runs at all outside Development remains a finding, but it does not break the cold start - see [api-testing.md](https://github.com/NikolaCokic/simpleshare-deployment/blob/main/docs/api-testing.md#findings-worth-fixing-before-tests-land) |

---

## Out of scope

Better served by plain unit tests - milliseconds instead of seconds per case, and far easier to
enumerate exhaustively:

- `BalanceService` pairwise netting and debt simplification maths
- `TokenService` claim construction
- Rounding edge tables (`0.01`, `0.005`, three-way and seven-way splits)

Integration tests prove the wiring. Once a behaviour is proven reachable over HTTP once, push its
edge cases down to unit tests.
