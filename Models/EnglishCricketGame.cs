namespace J1sDartSharp.Models;

/// <summary>One trip to the oche in English cricket.</summary>
/// <param name="Batting">True for a batting trip, false for bowling.</param>
/// <param name="Total">Batting: the three-dart total.</param>
/// <param name="Runs">Batting: runs scored (total over 40).</param>
/// <param name="Wickets">Bowling: wickets taken.</param>
public sealed record EnglishTrip(int Innings, bool Batting, int Total, int Runs, int Wickets, int Darts);

/// <summary>One player in English cricket.</summary>
public sealed class EnglishPlayer
{
    private readonly List<EnglishTrip> _trips = [];

    internal EnglishPlayer(int index, string name)
    {
        Index = index;
        Name = name;
    }

    public int Index { get; }

    public string Name { get; set; }

    public int Runs { get; internal set; }

    public int WicketsTaken { get; internal set; }

    public int DartsThrown { get; internal set; }

    public IReadOnlyList<EnglishTrip> Trips => _trips;

    public int OcheTrips => _trips.Count;

    internal void AddTrip(EnglishTrip trip) => _trips.Add(trip);

    internal void Reset()
    {
        Runs = 0;
        WicketsTaken = 0;
        DartsThrown = 0;
        _trips.Clear();
    }
}

/// <summary>Where the current turn stands.</summary>
/// <param name="Total">Batting: three-dart total so far.</param>
/// <param name="Runs">Batting: runs so far this turn (total over 40).</param>
/// <param name="Wickets">Bowling: wickets taken so far this turn (capped at those left).</param>
/// <param name="EndsGame">This turn wins the chase.</param>
/// <param name="EndsInnings">This turn takes the last wicket.</param>
public readonly record struct EnglishTurnPreview(int Total, int Runs, int Wickets, bool EndsGame, bool EndsInnings);

/// <summary>
/// English cricket (match play only): batter vs bowler, then swap.
///
///   • Innings 1: side 0 bats, side 1 bowls. Innings 2: roles swap.
///   • Each round the batter throws first, then the bowler.
///   • Batter: runs = three-dart total minus 40 (nothing for 40 or less).
///   • Bowler: only the bull counts — 25 takes 1 wicket, bull (50) takes 2.
///   • An innings ends when all 10 wickets are down.
///   • Innings 2 ends early once the chasing batter passes the target.
///   • Most runs wins; equal runs is a tie.
///
/// Committed turns are replayed to rebuild state, so Back can step into a previous turn.
/// </summary>
public sealed class EnglishCricketGame
{
    public const int WicketsPerInnings = 10;
    public const int RunThreshold = 40;

    private sealed record Turn(DartThrow[] Darts, DateTime At);

    private readonly List<Turn> _turns = [];
    private readonly List<DartThrow> _darts = [];
    private readonly EnglishPlayer[] _players;

    public EnglishCricketGame(IReadOnlyList<string> names)
    {
        if (names.Count != 2)
            throw new ArgumentException("English cricket needs two players.", nameof(names));

        _players = names.Select((n, i) => new EnglishPlayer(i, n)).ToArray();
        ResetState();
    }

    public DateTime StartedAt { get; } = DateTime.Now;

    public IReadOnlyList<EnglishPlayer> Players => _players;

    /// <summary>1 or 2.</summary>
    public int Innings { get; private set; }

    /// <summary>True while the batter is throwing; false for the bowler.</summary>
    public bool BatterUp { get; private set; }

    public int WicketsLeft { get; private set; }

    public EnglishPlayer Batter => _players[Innings == 1 ? 0 : 1];

    public EnglishPlayer Bowler => _players[Innings == 1 ? 1 : 0];

    public EnglishPlayer Thrower => BatterUp ? Batter : Bowler;

    /// <summary>Runs the second batter needs to pass (innings 2 only).</summary>
    public int? Target => Innings == 2 ? _players[0].Runs + 1 : null;

    public IReadOnlyList<DartThrow> CurrentDarts => _darts;

    public bool IsOver { get; private set; }

    /// <summary>The winner, or null for a tie (when <see cref="IsOver"/>).</summary>
    public EnglishPlayer? Winner { get; private set; }

    public bool HasStarted => _turns.Count > 0 || _darts.Count > 0;

    public EnglishTurnPreview Live => Evaluate(_darts);

    public bool CanAddDart
    {
        get
        {
            if (IsOver || _darts.Count >= 3) return false;
            var live = Live;
            return !live.EndsGame && !live.EndsInnings;
        }
    }

    public bool CanEnter
    {
        get
        {
            if (IsOver) return false;
            var live = Live;
            return _darts.Count == 3 || live.EndsGame || live.EndsInnings;
        }
    }

    public bool CanUndo => _darts.Count > 0 || _turns.Count > 0;

    // ── Actions ──────────────────────────────────────────────────────────────

    public bool AddDart(DartThrow dart)
    {
        if (!CanAddDart) return false;
        _darts.Add(dart);
        return true;
    }

    public bool Enter()
    {
        if (!CanEnter) return false;
        var turn = new Turn(_darts.ToArray(), DateTime.Now);
        _turns.Add(turn);
        _darts.Clear();
        Apply(turn);
        return true;
    }

    /// <summary>
    /// Remove the last dart of this turn. With nothing entered, step back into the
    /// previous turn (removing its last dart), which also reopens a finished game.
    /// </summary>
    public bool Undo()
    {
        if (_darts.Count > 0)
        {
            _darts.RemoveAt(_darts.Count - 1);
            return true;
        }

        if (_turns.Count == 0)
            return false;

        var last = _turns[^1];
        _turns.RemoveAt(_turns.Count - 1);
        Replay();

        _darts.AddRange(last.Darts);
        if (_darts.Count > 0)
            _darts.RemoveAt(_darts.Count - 1);
        return true;
    }

    // ── Scoring ──────────────────────────────────────────────────────────────

    public static int WicketsFor(DartThrow dart) =>
        dart.Number == 25 ? dart.Multiplier : 0;

    private EnglishTurnPreview Evaluate(IReadOnlyList<DartThrow> darts)
    {
        if (BatterUp)
        {
            var total = darts.Sum(d => d.Score);
            var runs = Math.Max(0, total - RunThreshold);
            var wins = Target is int target && Batter.Runs + runs >= target;
            return new EnglishTurnPreview(total, runs, 0, wins, false);
        }

        var wickets = Math.Min(WicketsLeft, darts.Sum(WicketsFor));
        return new EnglishTurnPreview(0, 0, wickets, false, wickets >= WicketsLeft);
    }

    private void Apply(Turn turn)
    {
        var result = Evaluate(turn.Darts);
        var thrower = Thrower;
        thrower.DartsThrown += turn.Darts.Length;

        if (BatterUp)
        {
            thrower.Runs += result.Runs;
            thrower.AddTrip(new EnglishTrip(Innings, true, result.Total, result.Runs, 0, turn.Darts.Length));

            if (result.EndsGame)
            {
                IsOver = true;
                Winner = thrower;
                return;
            }

            BatterUp = false;
            return;
        }

        thrower.WicketsTaken += result.Wickets;
        thrower.AddTrip(new EnglishTrip(Innings, false, 0, 0, result.Wickets, turn.Darts.Length));
        WicketsLeft -= result.Wickets;
        BatterUp = true;

        if (WicketsLeft > 0)
            return;

        if (Innings == 1)
        {
            Innings = 2;
            WicketsLeft = WicketsPerInnings;
            return;
        }

        // Innings 2 all out: most runs wins.
        IsOver = true;
        var (first, second) = (_players[0], _players[1]);
        Winner = first.Runs == second.Runs ? null : first.Runs > second.Runs ? first : second;
    }

    private void ResetState()
    {
        Innings = 1;
        BatterUp = true;
        WicketsLeft = WicketsPerInnings;
        IsOver = false;
        Winner = null;
    }

    private void Replay()
    {
        foreach (var p in _players)
            p.Reset();
        ResetState();

        foreach (var turn in _turns)
            Apply(turn);
    }
}
