namespace J1sDartSharp.Models;

/// <summary>
/// One side in an X01 game: the practice player, or Home / Away in match play.
/// State is owned by <see cref="X01Game"/>; everything here is read-only to callers
/// except <see cref="Name"/>.
/// </summary>
public sealed class X01Side
{
    private readonly List<X01OcheEntry> _trips = [];

    internal X01Side(int index, string name, int startScore, bool startsOpen)
    {
        Index = index;
        Name = name;
        StartScore = startScore;
        Reset(startsOpen);
    }

    public int Index { get; }

    public string Name { get; set; }

    public int StartScore { get; }

    public int Remaining { get; internal set; }

    /// <summary>True once the side is "in" (always true for single in).</summary>
    public bool IsOpen { get; internal set; }

    /// <summary>Darts thrown up to and including the opening double (double in only).</summary>
    public int DartsToOpen { get; internal set; }

    public DateTime? OpenedAt { get; internal set; }

    /// <summary>Every dart thrown, including misses and double-in attempts.</summary>
    public int DartsThrown { get; internal set; }

    public int Busts { get; internal set; }

    /// <summary>Every trip to the oche, oldest first.</summary>
    public IReadOnlyList<X01OcheEntry> Trips => _trips;

    public int OcheTrips => _trips.Count;

    public int PointsScored => StartScore - Remaining;

    public double ThreeDartAverage => DartsThrown > 0 ? PointsScored * 3.0 / DartsThrown : 0;

    public int FinishingDart { get; internal set; }

    public DateTime? FinishedAt { get; internal set; }

    public bool HasFinished => FinishedAt is not null;

    internal void AddTrip(X01OcheEntry entry) => _trips.Add(entry);

    internal void Reset(bool startsOpen)
    {
        Remaining = StartScore;
        IsOpen = startsOpen;
        DartsToOpen = 0;
        OpenedAt = null;
        DartsThrown = 0;
        Busts = 0;
        FinishingDart = 0;
        FinishedAt = null;
        _trips.Clear();
    }
}

/// <summary>What the darts entered so far in a turn add up to.</summary>
/// <param name="Scored">Points scored this turn (0 once bust).</param>
/// <param name="Remaining">Remaining after these darts (the pre-turn score if bust).</param>
/// <param name="OpenedOnDart">1-based dart that achieved the double in, if it happened this turn.</param>
public readonly record struct X01TurnPreview(int Scored, int Remaining, bool IsBust, bool IsFinished, int? OpenedOnDart);

/// <summary>
/// X01 scoring engine for practice (one side) or match play (two sides,
/// alternating turns). Plain C# with no UI types.
///
/// Committed turns are kept as a list and the side totals are rebuilt by
/// replaying them, so Back can step into a previous turn safely.
///
/// Rules:
///   • Double in: darts before the opening double score nothing (they still count as darts).
///   • Double out: finishing needs a double (bull 50 counts); leaving 1 is a bust.
///   • Bust: going below 0, leaving 1 (double out), or reaching 0 without a
///     legal finishing dart. A bust scores nothing and the turn ends.
///   • A turn is entered once 3 darts are in, or on a bust or a finish.
/// </summary>
public sealed class X01Game
{
    private sealed record Turn(int Side, DartThrow[] Darts, bool ManualBust, DateTime At);

    private readonly List<Turn> _turns = [];
    private readonly List<DartThrow> _darts = [];
    private readonly X01Side[] _sides;

    public X01Game(int startScore, InMode inMode, OutMode outMode, IReadOnlyList<string> sideNames)
    {
        if (sideNames.Count is < 1 or > 2)
            throw new ArgumentException("X01 supports one or two sides.", nameof(sideNames));

        StartScore = startScore;
        InMode = inMode;
        OutMode = outMode;
        _sides = sideNames.Select((name, i) => new X01Side(i, name, startScore, StartsOpen)).ToArray();
    }

    public int StartScore { get; }

    public InMode InMode { get; }

    public OutMode OutMode { get; }

    public DateTime StartedAt { get; } = DateTime.Now;

    public string ModeTag =>
        $"{(InMode == InMode.SingleIn ? 'S' : 'D')}I{(OutMode == OutMode.SingleOut ? 'S' : 'D')}O";

    public IReadOnlyList<X01Side> Sides => _sides;

    public int CurrentSideIndex { get; private set; }

    public X01Side CurrentSide => _sides[CurrentSideIndex];

    /// <summary>Darts entered so far in the current turn.</summary>
    public IReadOnlyList<DartThrow> CurrentDarts => _darts;

    public X01Side? Winner { get; private set; }

    public bool IsOver => Winner is not null;

    /// <summary>True once any dart has been entered; setup is locked from then on.</summary>
    public bool HasStarted => _turns.Count > 0 || _darts.Count > 0;

    /// <summary>The current turn so far.</summary>
    public X01TurnPreview Live => Evaluate(CurrentSide, _darts);

    public int DartsLeftInTurn => 3 - _darts.Count;

    public bool CanAddDart
    {
        get
        {
            if (IsOver || _darts.Count >= 3) return false;
            var live = Live;
            return !live.IsBust && !live.IsFinished;
        }
    }

    public bool CanEnter
    {
        get
        {
            if (IsOver) return false;
            var live = Live;
            return _darts.Count == 3 || live.IsBust || live.IsFinished;
        }
    }

    public bool CanDeclareBust => !IsOver && !Live.IsFinished;

    public bool CanUndo => _darts.Count > 0 || _turns.Count > 0;

    /// <summary>True if the side is (or, mid-turn, has just become) in.</summary>
    public bool IsOpenNow(X01Side side) =>
        side.IsOpen || (side == CurrentSide && Live.OpenedOnDart is not null);

    // ── Actions ──────────────────────────────────────────────────────────────

    public bool AddDart(DartThrow dart)
    {
        if (!CanAddDart) return false;
        _darts.Add(dart);
        return true;
    }

    /// <summary>Enter the turn (3 darts, a bust, or a finish).</summary>
    public bool Enter()
    {
        if (!CanEnter) return false;
        Commit(manualBust: false);
        return true;
    }

    /// <summary>End the turn as a bust, whatever has been entered.</summary>
    public bool DeclareBust()
    {
        if (!CanDeclareBust) return false;
        Commit(manualBust: true);
        return true;
    }

    /// <summary>
    /// Remove the last dart of this turn. With no darts entered, step back into
    /// the previous turn (removing its last dart), which also reopens a finished game.
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

        CurrentSideIndex = last.Side;
        _darts.AddRange(last.Darts);

        // A manual bust is undone by reopening the turn as it was; otherwise
        // Back also takes off that turn's last dart (one tap = one dart).
        if (!last.ManualBust && _darts.Count > 0)
            _darts.RemoveAt(_darts.Count - 1);

        return true;
    }

    /// <summary>Snapshot of side 0 as a practice session for history.</summary>
    public X01Session ToPracticeSession()
    {
        var side = _sides[0];
        var session = new X01Session(StartScore, InMode, OutMode)
        {
            StartedAt = StartedAt
        };

        session.DoubleInAchieved = side.IsOpen;
        session.DoubleInAt = InMode == InMode.SingleIn ? StartedAt : side.OpenedAt;
        session.DartsToDoubleIn = InMode == InMode.DoubleIn ? side.DartsToOpen : 0;
        session.RemainingScore = side.Remaining;
        session.OcheLog.AddRange(side.Trips);
        session.DartsThrown = side.DartsThrown;
        session.BustCount = side.Busts;

        if (side.HasFinished)
        {
            session.DoubleOutAchieved = true;
            session.DoubleOutAt = side.FinishedAt;
            session.FinishingDart = side.FinishingDart;
            session.CompletedAt = side.FinishedAt;
            session.IsComplete = true;
        }

        return session;
    }

    // ── Scoring ──────────────────────────────────────────────────────────────

    private bool StartsOpen => InMode == InMode.SingleIn;

    private X01TurnPreview Evaluate(X01Side side, IReadOnlyList<DartThrow> darts)
    {
        var remaining = side.Remaining;
        var open = side.IsOpen;
        var scored = 0;
        int? openedOn = null;
        var doubleOut = OutMode == OutMode.DoubleOut;

        for (var i = 0; i < darts.Count; i++)
        {
            var dart = darts[i];

            if (!open)
            {
                // Double in: nothing counts until a double (incl. bull 50) lands.
                if (dart.Number == 0 || dart.Multiplier != 2)
                    continue;
                open = true;
                openedOn = i + 1;
            }

            var after = remaining - dart.Score;
            var bust = after < 0
                       || (doubleOut && after == 1)
                       || (after == 0 && doubleOut && dart.Multiplier != 2);

            if (bust)
                return new X01TurnPreview(0, side.Remaining, IsBust: true, IsFinished: false, openedOn);

            remaining = after;
            scored += dart.Score;

            if (remaining == 0)
                return new X01TurnPreview(scored, 0, IsBust: false, IsFinished: true, openedOn);
        }

        return new X01TurnPreview(scored, remaining, IsBust: false, IsFinished: false, openedOn);
    }

    private void Commit(bool manualBust)
    {
        var turn = new Turn(CurrentSideIndex, _darts.ToArray(), manualBust, DateTime.Now);
        _turns.Add(turn);
        _darts.Clear();

        Apply(turn);

        if (!IsOver)
            CurrentSideIndex = (turn.Side + 1) % _sides.Length;
    }

    private void Apply(Turn turn)
    {
        var side = _sides[turn.Side];
        var result = Evaluate(side, turn.Darts);

        // A bust declared with nothing entered still counts as a full visit.
        var darts = turn.Darts.Length > 0 ? turn.Darts.Length : 3;

        if (!side.IsOpen)
        {
            if (result.OpenedOnDart is int opened)
            {
                side.IsOpen = true;
                side.OpenedAt = turn.At;
                side.DartsToOpen += opened;
            }
            else
            {
                side.DartsToOpen += darts;
            }
        }

        side.DartsThrown += darts;
        var tripNumber = side.OcheTrips + 1;

        if (turn.ManualBust || result.IsBust)
        {
            side.Busts++;
            side.AddTrip(new X01OcheEntry(tripNumber, 0, side.Remaining, IsBust: true, darts));
            return;
        }

        side.Remaining = result.Remaining;
        side.AddTrip(new X01OcheEntry(tripNumber, result.Scored, result.Remaining, IsBust: false, darts));

        if (result.IsFinished)
        {
            side.FinishingDart = turn.Darts.Length;
            side.FinishedAt = turn.At;
            Winner = side;
        }
    }

    private void Replay()
    {
        foreach (var side in _sides)
            side.Reset(StartsOpen);

        Winner = null;
        CurrentSideIndex = 0;

        foreach (var turn in _turns)
        {
            Apply(turn);
            if (!IsOver)
                CurrentSideIndex = (turn.Side + 1) % _sides.Length;
        }
    }
}
