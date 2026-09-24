using J1sDartSharp.Core.Models;

namespace J1sDartSharp.Core.Services;

/// <summary>
/// Port of app/services/pairing_service.rb — assigns available players to the
/// 11-game slate.
///
/// Differences from Rails are structural only:
///   • Pure: takes players and games as input instead of querying the DB, and
///     returns the schedule instead of writing it. The Api loads, calls this,
///     and persists the result (Rails' assign!/clear_reassignable_games!).
///   • Stateless: all working state lives in a per-call object, so a single
///     instance is thread-safe and can be registered as a singleton.
///   • Synchronous: it is CPU-only work on a few dozen items; no I/O to await.
///
/// The scheduling RULES are ported as-is, including behavior that looks
/// unintended. Decision: keep Rails parity until the app is running, then
/// revisit. See RULES TO REVISIT below.
///
/// ═══════════════════════════════════════════════════════════════════════════
/// OFFICIAL LEAGUE RULES (from the Rails header)
///   • 4–8 players; at least 1 woman must be available
///   • A woman must play ≥1 singles, ≥1 doubles, and the team game
///   • No two players partner together in more than one doubles game
///   • No player appears in both games of the same game type
///   • Load balancing: 20 slots spread as evenly as possible; always pick the
///     least-loaded eligible player(s), ties broken by rank (1 first)
///
/// RULES TO REVISIT (Rails behavior preserved on purpose)
///   1. Female doubles coverage is effectively never enforced. The "must place
///      a woman now" check only fires when ≤1 doubles game remains, but
///      "remaining" is computed from game statuses that the reassign flow has
///      just reset to Unassigned — so it is always 6. A woman lands in doubles
///      only if load order happens to put her there.
///   2. Female singles coverage is a preference, not a guarantee: a woman is
///      only chosen if her load is within 1 of the least-loaded player.
///   3. Completed games are scheduled like any other game. Rails' reassign
///      kept completed games' participants and then added new ones on top.
///      The Api port must decide how to handle this (skip completed games).
///   4. Tie-breaks: Rails' sort_by is not stable, so equal-load/equal-rank
///      ties were resolved arbitrarily. Here they resolve by player name
///      (ordinal), matching the order Rails loaded players in.
/// ═══════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PairingService
{
    /// <summary>
    /// Builds the full schedule for a match.
    /// </summary>
    /// <param name="availablePlayers">
    /// Active players not marked absent for the match (Rails: Player.available_for).
    /// Must be persisted (distinct, non-zero Ids).
    /// </param>
    /// <param name="games">
    /// The match's games. Statuses matter (see RULES TO REVISIT #1): to mirror
    /// Rails' assign!, the caller resets Assigned games to Unassigned first.
    /// </param>
    /// <exception cref="InsufficientPlayersException">Too few players, or no women available.</exception>
    /// <exception cref="PairingImpossibleException">A game cannot be filled under the rules.</exception>
    public PairingResult BuildSchedule(IEnumerable<Player> availablePlayers, IEnumerable<Game> games)
    {
        ArgumentNullException.ThrowIfNull(availablePlayers);
        ArgumentNullException.ThrowIfNull(games);

        // Rails loaded players ordered by name; keep that as the base order.
        var players = availablePlayers.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        var orderedGames = games.OrderBy(g => g.Sequence).ToList();

        EnsureUsablePlayerIds(players);
        Validate(players);

        return new ScheduleRun(players, orderedGames).Execute();
    }

    // ── Validation (Rails: validate!) ────────────────────────────────────────

    private static void Validate(IReadOnlyList<Player> players)
    {
        var n = players.Count;

        if (n < LeagueRules.MinRoster)
            throw new InsufficientPlayersException(
                $"{n} player(s) available; minimum is {LeagueRules.MinRoster}.");

        if (!players.Any(p => p.IsFemale))
            throw new InsufficientPlayersException(
                "No female players are available. At least one woman must play each match.");

        // 6 doubles games need 6 unique pairs; C(n,2) must be ≥ 6.
        var minForDoubles = MinPlayersForUniqueDoubles(LeagueRules.DoublesGameCount);
        if (n < minForDoubles)
            throw new PairingImpossibleException(
                $"Need at least {minForDoubles} players to fill {LeagueRules.DoublesGameCount} unique doubles pairs; " +
                $"only {n} available.");
    }

    /// <summary>Smallest n such that C(n,2) ≥ doublesCount.</summary>
    private static int MinPlayersForUniqueDoubles(int doublesCount)
    {
        var n = 2;
        while (n * (n - 1) / 2 < doublesCount) n++;
        return n;
    }

    private static void EnsureUsablePlayerIds(IReadOnlyList<Player> players)
    {
        if (players.Any(p => p.Id <= 0))
            throw new ArgumentException("All players must be persisted (Id > 0).", nameof(players));

        if (players.Select(p => p.Id).Distinct().Count() != players.Count)
            throw new ArgumentException("Duplicate players supplied.", nameof(players));
    }

    // ── One scheduling pass (Rails: build_schedule and its pickers) ─────────

    private sealed class ScheduleRun
    {
        private readonly IReadOnlyList<Player> _players;
        private readonly IReadOnlyList<Game> _games;

        private readonly Dictionary<int, int> _load;                          // playerId → games so far
        private readonly HashSet<(int, int)> _usedPairs = [];                 // (lowId, highId) already together
        private readonly Dictionary<GameType, HashSet<int>> _typeUsage = new(); // game type → playerIds

        private bool _femaleSingles;
        private bool _femaleDoubles;
        // Rails also tracked female_cover[:team] but never read it — omitted.

        public ScheduleRun(IReadOnlyList<Player> players, IReadOnlyList<Game> games)
        {
            _players = players;
            _games = games;
            _load = players.ToDictionary(p => p.Id, _ => 0);
        }

        public PairingResult Execute()
        {
            var assignments = new List<GameAssignment>(_games.Count);

            foreach (var game in _games)
            {
                var picked = game.Format switch
                {
                    GameFormat.Singles => PickSingles(game),
                    GameFormat.Doubles => PickDoubles(game),
                    GameFormat.Team => PickTeam(),
                    _ => throw new InvalidOperationException($"Unknown format {game.Format}.")
                };

                foreach (var p in picked) _load[p.Id]++;

                TypeUsage(game.GameType).UnionWith(picked.Select(p => p.Id));

                for (var i = 0; i < picked.Count; i++)
                    for (var j = i + 1; j < picked.Count; j++)
                        _usedPairs.Add(PairKey(picked[i], picked[j]));

                assignments.Add(new GameAssignment(game, picked));
            }

            return new PairingResult(assignments, new Dictionary<int, int>(_load));
        }

        // ── Singles ─────────────────────────────────────────────────────────
        // Least-loaded player who hasn't played this game type. While female
        // singles coverage is unmet, take a woman if her load is within 1 of
        // the minimum (RULES TO REVISIT #2).

        private List<Player> PickSingles(Game game)
        {
            var alreadyPlayed = TypeUsage(game.GameType);

            var pool = _players
                .Where(p => !alreadyPlayed.Contains(p.Id))
                .OrderBy(p => _load[p.Id])
                .ThenBy(p => p.Rank)
                .ToList();

            if (pool.Count == 0)
                throw new PairingImpossibleException(
                    $"No eligible player left for {game.DisplayName} (game {game.Sequence}).");

            Player player;
            if (!_femaleSingles)
            {
                var woman = pool.FirstOrDefault(p => p.IsFemale);
                var minLoad = _load[pool[0].Id];

                if (woman is not null && _load[woman.Id] <= minLoad + 1)
                {
                    _femaleSingles = true;
                    player = woman;
                }
                else
                {
                    player = pool[0];
                }
            }
            else
            {
                player = pool[0];
            }

            if (player.IsFemale) _femaleSingles = true;
            return [player];
        }

        // ── Doubles ─────────────────────────────────────────────────────────
        // Two least-loaded players who haven't played this game type and
        // haven't partnered yet. A woman is forced only on the last remaining
        // doubles game (RULES TO REVISIT #1).

        private List<Player> PickDoubles(Game game)
        {
            var alreadyPlayed = TypeUsage(game.GameType);

            var doublesGames = _games.Where(g => g.Format == GameFormat.Doubles).ToList();
            var remainingDoubles = doublesGames.Count - doublesGames.Count(g => g.Status == GameStatus.Assigned);

            var needFemaleNow = !_femaleDoubles && remainingDoubles <= 1;

            var pair = PickPair(alreadyPlayed, requireFemale: needFemaleNow)
                ?? throw new PairingImpossibleException(
                    $"Cannot build a unique doubles pair for {game.DisplayName} from the available players.");

            if (pair.Any(p => p.IsFemale)) _femaleDoubles = true;
            return pair;
        }

        /// <summary>
        /// Rails: pick_best_pair / pick_pair_with_female. Walks pairs in the
        /// same order as Ruby's combination(2) and keeps the first pair with
        /// the lowest (combined load, combined rank) — same as min_by.
        /// </summary>
        private List<Player>? PickPair(HashSet<int> alreadyPlayed, bool requireFemale)
        {
            var candidates = _players.Where(p => !alreadyPlayed.Contains(p.Id)).ToList();

            List<Player>? best = null;
            (int Load, int Rank) bestKey = default;

            for (var i = 0; i < candidates.Count; i++)
            {
                for (var j = i + 1; j < candidates.Count; j++)
                {
                    var a = candidates[i];
                    var b = candidates[j];

                    if (requireFemale && !a.IsFemale && !b.IsFemale) continue;
                    if (_usedPairs.Contains(PairKey(a, b))) continue;

                    var key = (Load: _load[a.Id] + _load[b.Id], Rank: a.Rank + b.Rank);

                    if (best is null || key.CompareTo(bestKey) < 0)
                    {
                        best = [a, b];
                        bestKey = key;
                    }
                }
            }

            return best;
        }

        // ── Team ────────────────────────────────────────────────────────────
        // Four least-loaded players. If none is a woman, swap the most-loaded
        // of the four for the least-loaded woman outside the group.

        private List<Player> PickTeam()
        {
            var byLoad = _players
                .OrderBy(p => _load[p.Id])
                .ThenBy(p => p.Rank)
                .ToList();

            var candidates = byLoad.Take(LeagueRules.TeamSize).ToList();

            if (!candidates.Any(p => p.IsFemale))
            {
                var woman = byLoad.FirstOrDefault(p => p.IsFemale && !candidates.Contains(p));
                var swapOut = candidates.MaxBy(p => _load[p.Id])!;

                candidates.Remove(swapOut);
                // Validate() guarantees a woman exists, so this is always non-null;
                // Rails would have silently fielded 3 players if it weren't.
                if (woman is not null) candidates.Add(woman);
            }

            return candidates.Take(LeagueRules.TeamSize).ToList();
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private HashSet<int> TypeUsage(GameType type)
        {
            if (!_typeUsage.TryGetValue(type, out var set))
            {
                set = [];
                _typeUsage[type] = set;
            }
            return set;
        }

        private static (int, int) PairKey(Player a, Player b) =>
            a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id);
    }
}
