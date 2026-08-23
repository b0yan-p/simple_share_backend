---
name: api-integration-tests
description: Write and maintain integration tests for the SimpleShare API. Use whenever an endpoint is added, changed or removed - any edit to Controllers/, Contracts/, or to validation and authorization behaviour in Services/ - and whenever asked to add, fix or run API tests. The test project already exists at tests/SimpleShare.IntegrationTests.
---

# API integration tests

Tests are part of the change, not a follow-up task. An endpoint added, changed or removed without
its tests moving in the same commit is an incomplete change.

The project already exists at `tests/SimpleShare.IntegrationTests` (xUnit v3, `WebApplicationFactory`,
real Postgres via Testcontainers, Shouldly). Do not scaffold, re-tool or restructure it - only add
tests to it.

## When this applies

- a new route, or a route removed
- a changed route template, HTTP verb, status code or response shape
- a new or renamed field in `Contracts/`
- a new, renamed or removed `AppException` code thrown from `Services/`
- an `[Authorize]` attribute added or removed
- a change to who may touch a group, expense or member

Pure refactors with no HTTP-visible change need no new tests, but the suite must still pass.

---

## Rules

1. **Black box over HTTP.** Drive the API through `HttpClient`. Never call a service class directly
   from an integration test - that belongs in a unit test.
2. **Assert the `code`, not just the status.** Errors come back from `GlobalExceptionHandler` as
   `application/problem+json` with a `code` extension. The mobile client branches on that string, so
   it is the real contract. A test asserting only `400` passes straight through a rename that breaks
   the app.
3. **Every authenticated route needs an anonymous-401 case**, added to the table-driven test in
   `AuthorizationMatrixTests.cs` rather than written as a one-off.
4. **Every route taking a `{groupId}` needs a cross-tenant case** in the same file: a second
   authenticated user must get `403` or `404` - never `200`, never `500`.
5. **Unique data per test.** Each test registers its own user. Do not reset the database between
   tests, do not depend on `DbSeeder` accounts, do not share state across tests.
6. **Real tokens only.** Get them from `POST /api/Auth/register` via the fixture helper. Never mint
   a JWT inside a test - the issuer, audience and signing-key wiring is part of what is under test.
7. **One subject per test.** Name them `Method_Scenario_Expectation`, with the case ID from
   `docs/api-test-cases.md` in a comment.
8. **Arrange / Act / Assert.** Every test is three blocks in that order, separated by a blank line
   and labelled `// Arrange`, `// Act`, `// Assert`. Specifically:
   - **Arrange** - everything that builds the world: registering users, creating groups, adding
     members, seeding expenses, and the request body. Setup that happens over HTTP is still Arrange.
   - **Act** - *exactly one* call: the request under test. If a test needs two Acts, it is two tests.
   - **Assert** - the response, plus any read-back needed to prove the side effect. A follow-up `GET`
     used to verify a write belongs in Assert, not in a second Act.
   - No assertions in Arrange. If setup must verify something, that check lives inside a helper in
     `Extensions.cs` (as `RegisterAsync` does), keeping the test body's Assert block the only place
     expectations appear.
9. **Pass `TestContext.Current.CancellationToken`** to every call that accepts one - `GetAsync`,
   `PostAsJsonAsync`, `ReadFromJsonAsync` and friends. Without it a cancelled or timed-out run keeps
   waiting on live HTTP calls, and the xUnit analyzer flags it as
   [xUnit1051](https://xunit.net/xunit.analyzers/rules/xUnit1051). Fixture lifetime methods
   (`InitializeAsync`, `DisposeAsync`) are outside a test and do not take it.

### Invariants worth asserting everywhere

- **Group balances sum to exactly `0`** after any sequence of expenses, updates, deletions and
  settle-ups. Assert it at the end of every balance-touching test - it catches rounding drift that
  per-pair assertions miss.
- **No `500` on any input a client can send.** Malformed GUID, unknown ID, missing field and hostile
  payload all have defined `4xx` answers.
- **Every error carries `code` and `traceId`.**

---

## Where tests go

One file per controller, plus the matrix file:

```
tests/SimpleShare.IntegrationTests/
  ApiFixture.cs                 # Postgres container + host   (shared, do not edit per-test)
  ApiCollection.cs              # xUnit collection definition (shared)
  Extensions.cs                 # register/auth + assertion helpers
  AuthTests.cs
  GroupTests.cs
  GroupMemberTests.cs
  ExpenseTests.cs
  BalanceTests.cs
  ActivityTests.cs
  AuthorizationMatrixTests.cs   # rules 3 and 4
```

A new controller gets a new file. Extend `Extensions.cs` with shared setup helpers
(`CreateGroupAsync`, `AddMemberAsync`, `AddExpenseAsync`) as soon as a second test needs one, so test
bodies read as scenario, action, assertion and nothing else.

## Using the shared infrastructure

`ApiFixture` owns the Postgres container and the in-process host. Take it as a constructor parameter
and join the collection - never construct a `WebApplicationFactory` or a container yourself.

```csharp
[Collection(nameof(ApiCollection))]
public sealed class ExpenseTests(ApiFixture fixture)
{
    [Fact] // EXP-09
    public async Task CreateExpense_SplitsDoNotSumToTotal_ReturnsSplitsSumInvalid()
    {
        // Arrange
        // RegisterAsync creates a fresh user and returns a client with its bearer token set.
        var (client, user) = await fixture.RegisterAsync();
        var group = await client.CreateGroupAsync("Trip");
        var request = new
        {
            Description = "Dinner",
            ExpenseDate = DateTime.UtcNow,
            TotalAmount = 30m,
            Payments = new[] { new { MemberId = user.MemberId, Amount = 30m } },
            Splits    = new[] { new { MemberId = user.MemberId, Amount = 20m } },  // wrong on purpose
        };

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/Expense/{group.Id}", request, TestContext.Current.CancellationToken);

        // Assert
        // Checks status, problem+json content type, the `code` and the presence of traceId.
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "splits_sum_invalid");
    }
}
```

---

## Per-change checklist

**Endpoint added**

- [ ] Happy path: correct status and response shape
- [ ] One case per validation branch, asserting the `code`
- [ ] Added to the anonymous-401 matrix if it is `[Authorize]`
- [ ] Added to the cross-tenant matrix if it takes a `{groupId}`
- [ ] Non-existent GUID gives `404` with a `code`, not `500`
- [ ] If it writes: the read endpoint reflects the write, the balance invariant still holds, and an
      `Activity` row is produced with the right `ActivityType`
- [ ] Row added to `docs/api-test-cases.md`

**Endpoint changed**

- [ ] Existing tests updated in the same commit, never deleted to make the build green
- [ ] A renamed error `code` renamed in the assertions too
- [ ] A changed status code is a client-visible break - say so in the commit message
- [ ] New contract fields get at least a required/missing case

**Endpoint removed**

- [ ] Its tests deleted, not commented out
- [ ] Removed from both matrices and from `docs/api-test-cases.md`
- [ ] Anything reaching it indirectly through a helper is re-pointed

`docs/api-test-cases.md` is the coverage catalogue and the source of case IDs. **Update it in the
same commit as the code** - the endpoint inventory table first, then the case rows. A route missing
from that table is a route nobody knows is untested.

---

## Running

```bash
dotnet test                                        # everything
dotnet test --filter FullyQualifiedName~Expense    # one area
```

**Docker must be running** - Testcontainers starts a real Postgres. Without it, `dotnet test` fails
at container startup while `dotnet build` and `dotnet run` stay fine.

CI runs the same command on pull requests and on `main`. The publish job does **not** currently
depend on it, so a red suite is visible but does not block the image. That is deliberate while the
suite is being calibrated, and it does not make a red test optional: fix it, or say in the PR why it
is expected to fail.

---

## Do not

- Substitute `UseInMemoryDatabase` or SQLite for the Postgres container. They hide exactly the EF,
  migration and `decimal` behaviour that matters here.
- `Skip` or delete a failing test to get a green run. Cases asserting not-yet-implemented behaviour
  stay failing on purpose.
- Add `public partial class Program;` to `Program.cs`. It was required through .NET 8; on .NET 10 the
  class is already public and adding it trips
  [ASP0027](https://learn.microsoft.com/en-us/aspnet/core/diagnostics/asp0027).
- Put pure arithmetic here. Balance netting, rounding tables and claim construction belong in unit
  tests - orders of magnitude cheaper per case. Integration tests prove the wiring; once a behaviour
  is reachable over HTTP once, push its edge cases down.
