using System.Collections.Concurrent;

namespace J1sDartSharp.Services;

/// <summary>
/// X01 finishing routes, generated from the board rather than a hand-written
/// table, so every route is legal for the out rule and the darts left in the turn.
///
/// Labels: "20" single, "D20" double, "T20" treble, "25" outer bull, "DB" bull (50).
///
/// Ranking: fewer darts first, then routes that are easier to hit
/// (singles and T20/T19 set-ups, favourite doubles D20/D16 to finish), then
/// the higher opening dart. Set-up darts are listed highest first.
/// </summary>
public static class CheckoutService
{
    /// <summary>Routes shown per side on the 01 screen.</summary>
    public const int MaxRoutes = 3;

    private sealed record Dart(string Label, int Score, int Multiplier, int SetupCost);

    private static readonly Dart[] Board = BuildBoard();

    private static readonly ConcurrentDictionary<(int Remaining, int Darts, bool DoubleOut), string[][]> Cache = new();

    /// <summary>
    /// Best finishing routes for <paramref name="remaining"/> using at most
    /// <paramref name="dartsLeft"/> darts. Empty when there is no finish.
    /// </summary>
    public static IReadOnlyList<string[]> GetRoutes(
        int remaining, int dartsLeft = 3, bool doubleOut = true, int max = MaxRoutes)
    {
        dartsLeft = Math.Min(dartsLeft, 3);
        if (remaining <= 0 || dartsLeft <= 0 || max <= 0 || remaining > dartsLeft * 60)
            return [];

        var all = Cache.GetOrAdd((remaining, dartsLeft, doubleOut), key => Build(key.Remaining, key.Darts, key.DoubleOut));
        return all.Length <= max ? all : all[..max];
    }

    public static bool IsFinishable(int remaining, int dartsLeft = 3, bool doubleOut = true) =>
        GetRoutes(remaining, dartsLeft, doubleOut, 1).Count > 0;

    // ── Generation ───────────────────────────────────────────────────────────

    private static string[][] Build(int remaining, int dartsLeft, bool doubleOut)
    {
        var found = new List<(string[] Route, int Cost, int Lead)>();
        var seen = new HashSet<string>();

        var finishers = Board.Where(d => !doubleOut || d.Multiplier == 2)
                             .GroupBy(d => d.Score)
                             .ToDictionary(g => g.Key, g => g.ToArray());

        void TryFinish(int need, params Dart[] setup)
        {
            if (!finishers.TryGetValue(need, out var options))
                return;

            foreach (var last in options)
            {
                // Double out: the double is always last. Single out: any dart can
                // finish, so order the whole route highest first and drop repeats
                // (T17 DB and DB T17 are the same route).
                var darts = doubleOut
                    ? setup.OrderByDescending(d => d.Score).ThenByDescending(d => d.Multiplier).Append(last).ToArray()
                    : setup.Append(last).OrderByDescending(d => d.Score).ThenByDescending(d => d.Multiplier).ToArray();

                var route = darts.Select(d => d.Label).ToArray();
                if (!seen.Add(string.Join(' ', route)))
                    continue;

                var cost = setup.Sum(d => d.SetupCost) + FinishCost(last, doubleOut);
                found.Add((route, cost, darts[0].Score));
            }
        }

        // One dart
        TryFinish(remaining);

        // Two darts
        if (dartsLeft >= 2)
        {
            foreach (var a in Board)
            {
                var need = remaining - a.Score;
                if (need > 0)
                    TryFinish(need, a);
            }
        }

        // Three darts — set-up darts as an unordered pair (i <= j) so each combination appears once
        if (dartsLeft >= 3)
        {
            for (var i = 0; i < Board.Length; i++)
            {
                for (var j = i; j < Board.Length; j++)
                {
                    var need = remaining - Board[i].Score - Board[j].Score;
                    if (need > 0)
                        TryFinish(need, Board[i], Board[j]);
                }
            }
        }

        return found.OrderBy(r => r.Route.Length)
                    .ThenBy(r => r.Cost)
                    .ThenByDescending(r => r.Lead)
                    .Select(r => r.Route)
                    .ToArray();
    }

    private static int FinishCost(Dart dart, bool doubleOut)
    {
        if (doubleOut)
        {
            return dart.Label switch
            {
                "D20" or "D16" => 0,
                "D8" or "D10" or "D12" or "D18" => 1,
                "DB" => 3,
                _ => 2
            };
        }

        return dart.Multiplier switch
        {
            1 when dart.Score == 25 => 1,
            1 => 0,
            3 => 1,
            _ => dart.Label == "DB" ? 3 : 2
        };
    }

    private static Dart[] BuildBoard()
    {
        var darts = new List<Dart>();
        for (var n = 1; n <= 20; n++)
        {
            darts.Add(new Dart(n.ToString(), n, 1, SetupCost: 0));
            darts.Add(new Dart($"D{n}", n * 2, 2, SetupCost: 3));
            darts.Add(new Dart($"T{n}", n * 3, 3, SetupCost: 1));
        }

        darts.Add(new Dart("25", 25, 1, SetupCost: 2));
        darts.Add(new Dart("DB", 50, 2, SetupCost: 3));
        return darts.ToArray();
    }
}
