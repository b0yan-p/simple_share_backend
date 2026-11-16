namespace simpleShare_backend.Services.Calculations;

/// <summary>Pure, deterministic money projection. All graph amounts are integer cents.</summary>
public static class DebtGraphCalculator
{
    public sealed record Member(Guid Id, decimal Paid, decimal Owed);
    public sealed record RawDebt(Guid Debtor, Guid Creditor, decimal Amount);
    public sealed record Debt(Guid Debtor, Guid Creditor, long Cents);
    public sealed record Balance(Guid Id, long Paid, long Owed)
    {
        public long Net => checked(Paid - Owed);
    }
    public sealed record Result(IReadOnlyList<Balance> Members, IReadOnlyList<Debt> Regular, IReadOnlyList<Debt> Simplified);

    public static Result Calculate(IEnumerable<Member> members, IEnumerable<RawDebt> debts)
    {
        var people = members
            .OrderBy(x => x.Id)
            .ToArray();

        var raw = debts
            .OrderBy(x => x.Debtor)
            .ThenBy(x => x.Creditor)
            .ThenBy(x => x.Amount)
            .ToArray();

        if (
            people.Select(x => x.Id).Distinct().Count() != people.Length
            || people.Any(x => x.Paid < 0 || x.Owed < 0)
        )
        {
            throw new ArgumentException("Invalid members.");
        }

        var ids = people.Select(x => x.Id).ToHashSet();

        if (raw.Any(x => !ids.Contains(x.Debtor) || !ids.Contains(x.Creditor) || x.Amount < 0))
        {
            throw new ArgumentException("Invalid debt.");
        }

        // Zero edges carry no money and must not cross component-local dictionaries.
        // Validate endpoints first, then exclude them from every graph calculation.
        raw = raw.Where(x => x.Amount > 0).ToArray();

        var balances = new List<Balance>();
        var graph = new Dictionary<(Guid, Guid), long>();

        foreach (var component in Components(ids, raw.Select(x => (x.Debtor, x.Creditor))))
        {
            var rows = people.Where(x => component.Contains(x.Id)).ToArray();

            // No tolerance: accepting a discrepancy would silently repair financial data.
            if (rows.Sum(x => x.Paid) != rows.Sum(x => x.Owed))
            {
                throw new ArgumentException("Unbalanced financial data.");
            }

            var ledgerNet = component.ToDictionary(x => x, _ => 0m);
            foreach (var edge in raw.Where(x => component.Contains(x.Debtor)))
            {
                ledgerNet[edge.Debtor] -= edge.Amount;
                ledgerNet[edge.Creditor] += edge.Amount;
            }

            if (rows.Any(x => ledgerNet[x.Id] != x.Paid - x.Owed))
            {
                throw new ArgumentException("Ledger and member totals disagree.");
            }

            var total = checked((long)decimal.Round(rows.Sum(x => x.Paid) * 100, 0, MidpointRounding.AwayFromZero));
            var paid = Project(rows, x => x.Paid, total);
            var owed = Project(rows, x => x.Owed, total);

            balances.AddRange(rows.Select(x => new Balance(x.Id, paid[x.Id], owed[x.Id])));
            var pairs = new Dictionary<(Guid, Guid), decimal>();

            foreach (var e in raw.Where(x => component.Contains(x.Debtor) && x.Debtor != x.Creditor))
            {
                var forward = e.Debtor.CompareTo(e.Creditor) < 0;
                var key = forward ? (e.Debtor, e.Creditor) : (e.Creditor, e.Debtor);
                pairs[key] = pairs.GetValueOrDefault(key) + (forward ? e.Amount : -e.Amount);
            }

            foreach (var (key, amount) in pairs)
            {
                var cents = checked((long)decimal.Floor(amount * 100 + .5m));
                Add(graph, cents >= 0 ? key.Item1 : key.Item2, cents >= 0 ? key.Item2 : key.Item1, checked(Math.Abs(cents)));
            }

            var delta = rows.ToDictionary(x => x.Id, x => checked(paid[x.Id] - owed[x.Id]));

            foreach (var e in Edges(graph).Where(x => component.Contains(x.Debtor)))
            {
                delta[e.Debtor] = checked(delta[e.Debtor] + e.Cents);
                delta[e.Creditor] = checked(delta[e.Creditor] - e.Cents);
            }

            foreach (var e in Match(delta))
            {
                Add(graph, e.Debtor, e.Creditor, e.Cents);
            }
        }
        while (FindCycle(graph) is { } cycle)
        {
            var minimum = cycle.Min(x => graph[x]);
            foreach (var key in cycle) { graph[key] -= minimum; if (graph[key] == 0) graph.Remove(key); }
        }

        var regular = Edges(graph);
        var simple = new List<Debt>();

        foreach (var component in Components(ids, regular.Select(x => (x.Debtor, x.Creditor))))
        {
            simple.AddRange(Match(balances.Where(x => component.Contains(x.Id)).ToDictionary(x => x.Id, x => x.Net)));
        }

        var ordered = Sort(simple);
        return new Result([.. balances.OrderBy(x => x.Id)], regular, ordered.Count <= regular.Count ? ordered : regular);
    }

    private static Dictionary<Guid, long> Project(Member[] rows, Func<Member, decimal> amount, long total)
    {
        var result = rows.ToDictionary(x => x.Id, x => checked((long)decimal.Floor(amount(x) * 100)));
        var remaining = checked(total - result.Values.Sum());

        foreach (var row in rows.OrderByDescending(
            x => amount(x) * 100 - decimal.Floor(amount(x) * 100))
                .ThenBy(x => x.Id).Take(checked((int)remaining))
        )
        {
            result[row.Id] = checked(result[row.Id] + 1);
        }
        return result;
    }
    private static List<Debt> Match(Dictionary<Guid, long> net)
    {
        var result = new List<Debt>();
        var debtors = new PriorityQueue<Guid, (long, Guid)>();
        var creditors = new PriorityQueue<Guid, (long, Guid)>();

        foreach (var (id, value) in net)
        {
            if (value < 0) debtors.Enqueue(id, (value, id));
            if (value > 0) creditors.Enqueue(id, (checked(-value), id));
        }

        while (debtors.TryDequeue(out var debtor, out _))
        {
            var creditor = creditors.Dequeue();
            var amount = Math.Min(checked(-net[debtor]), net[creditor]);

            result.Add(new(debtor, creditor, amount));
            net[debtor] = checked(net[debtor] + amount);
            net[creditor] = checked(net[creditor] - amount);

            if (net[debtor] < 0)
            {
                debtors.Enqueue(debtor, (net[debtor], debtor));
            }

            if (net[creditor] > 0)
            {
                creditors.Enqueue(creditor, (checked(-net[creditor]), creditor));
            }
        }
        return result;
    }
    private static void Add(Dictionary<(Guid, Guid), long> graph, Guid d, Guid c, long amount)
    {
        if (amount == 0 || d == c) return;

        var opposite = graph.GetValueOrDefault((c, d));
        var cancel = Math.Min(opposite, amount);

        if (opposite > 0)
        {
            graph[(c, d)] -= cancel; if (graph[(c, d)] == 0) graph.Remove((c, d));
        }

        if (amount > cancel)
        {
            graph[(d, c)] = checked(graph.GetValueOrDefault((d, c)) + amount - cancel);
        }
    }

    private static List<Debt> Sort(IEnumerable<Debt> edges) =>
        edges.OrderByDescending(x => x.Cents)
             .ThenBy(x => x.Debtor)
             .ThenBy(x => x.Creditor)
             .ToList();

    private static List<Debt> Edges(Dictionary<(Guid, Guid), long> graph) => Sort(graph.Select(x => new Debt(x.Key.Item1, x.Key.Item2, x.Value)));
    private static List<HashSet<Guid>> Components(HashSet<Guid> ids, IEnumerable<(Guid A, Guid B)> edges)
    {
        var adjacent = ids.ToDictionary(x => x, _ => new HashSet<Guid>());

        foreach (var (a, b) in edges)
        {
            adjacent[a].Add(b); adjacent[b].Add(a);
        }

        var seen = new HashSet<Guid>(); var result = new List<HashSet<Guid>>();
        foreach (var id in ids.Order())
        {
            if (!seen.Add(id)) continue;
            var group = new HashSet<Guid>(); var stack = new Stack<Guid>(); stack.Push(id);
            while (stack.TryPop(out var v)) { group.Add(v); foreach (var next in adjacent[v].Order()) if (seen.Add(next)) stack.Push(next); }
            result.Add(group);
        }
        return result;
    }
    private static List<(Guid, Guid)>? FindCycle(Dictionary<(Guid, Guid), long> graph)
    {
        var state = new Dictionary<Guid, int>(); var path = new List<(Guid, Guid)>();
        List<(Guid, Guid)>? Visit(Guid v)
        {
            state[v] = 1;
            foreach (var key in graph.Keys.Where(x => x.Item1 == v).OrderBy(x => x.Item2))
            {
                if (state.GetValueOrDefault(key.Item2) == 1)
                {
                    var start = path.FindIndex(x => x.Item1 == key.Item2);
                    return (start < 0 ? new List<(Guid, Guid)>() : path.Skip(start).ToList()).Append(key).ToList();
                }

                if (state.GetValueOrDefault(key.Item2) == 0)
                {
                    path.Add(key); var found = Visit(key.Item2);

                    if (found != null)
                    {
                        return found;
                    }

                    path.RemoveAt(path.Count - 1);
                }
            }
            state[v] = 2; return null;
        }
        foreach (var v in graph.Keys.SelectMany(x => new[] { x.Item1, x.Item2 }).Distinct().Order())
        {
            if (state.GetValueOrDefault(v) == 0 && Visit(v) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
