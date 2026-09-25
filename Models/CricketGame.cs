namespace J1sDartSharp.Models;

/// <summary>Cricket variants. English is a separate game (batter vs bowler) with its own engine.</summary>
public enum CricketVariant { American, MickeyMouse, Mouse, English }

/// <summary>A row on the cricket board. Number is 15–20 or 25 (Bull); 0 for Mouse's T / D / 3B rows.</summary>
public sealed record CricketTarget(string Key, string Label, int Number)
{
    public bool IsSpecial => Number == 0;
}

/// <summary>Where one dart of a turn counts.</summary>
/// <param name="Options">Targets this dart could count on right now (more than one = the thrower can choose).</param>
/// <param name="Target">Where it counts; null for a dead dart (or part of a 3B trip).</param>
/// <param name="Marks">Marks it adds.</param>
public sealed record CricketPlacement(DartThrow Dart, IReadOnlyList<string> Options, string? Target, int Marks)
{
    public bool IsChoice => Options.Count > 1;
}

/// <summary>How a turn's darts are counted.</summary>
/// <param name="BedAvailable">Three darts in one bed and 3B still does something.</param>
/// <param name="BedIsChoice">The thrower can choose 3B or the individual darts.</param>
/// <param name="CountsAsBed">The trip is counted as one 3B mark.</param>
/// <param name="After">The thrower's state with this turn applied.</param>
public sealed record CricketTurnPlan(
    IReadOnlyList<CricketPlacement> Darts,
    bool BedAvailable,
    bool BedIsChoice,
    bool CountsAsBed,
    int Marks,
    bool Wins,
    CricketSide After);

/// <summary>One side's cricket state: the practice player, or Home / Away in match play.</summary>
public sealed class CricketSide
{
    private readonly Dictionary<string, int> _marks = [];

    internal CricketSide(int index, string name, IEnumerable<CricketTarget> targets)
    {
        Index = index;
        Name = name;
        foreach (var t in targets)
            _marks[t.Key] = 0;
    }

    private CricketSide(CricketSide other)
    {
        Index = other.Index;
        Name = other.Name;
        _marks = new Dictionary<string, int>(other._marks);
        Points = other.Points;
        DartsThrown = other.DartsThrown;
        MarksScored = other.MarksScored;
        OcheTrips = other.OcheTrips;
        FinishedAt = other.FinishedAt;
    }

    public int Index { get; }

    public string Name { get; set; }

    /// <summary>Marks toward closing each target (0–3).</summary>
    public IReadOnlyDictionary<string, int> Marks => _marks;

    public int Points { get; internal set; }

    public int DartsThrown { get; internal set; }

    /// <summary>All marks thrown on targets, including scoring marks after closing.</summary>
    public int MarksScored { get; internal set; }

    public int OcheTrips { get; internal set; }

    /// <summary>Marks per round (trip to the oche).</summary>
    public double Mpr => OcheTrips > 0 ? (double)MarksScored / OcheTrips : 0;

    public DateTime? FinishedAt { get; internal set; }

    public bool IsClosed(string key) => _marks.TryGetValue(key, out var m) && m >= 3;

    public int ClosedCount => _marks.Values.Count(m => m >= 3);

    public bool AllClosed => _marks.Values.All(m => m >= 3);

    internal void SetMarks(string key, int marks) => _marks[key] = marks;

    internal void Reset()
    {
        foreach (var key in _marks.Keys.ToList())
            _marks[key] = 0;
        Points = 0;
        DartsThrown = 0;
        MarksScored = 0;
        OcheTrips = 0;
        FinishedAt = null;
    }

    internal CricketSide Clone() => new(this);
}

/// <summary>
/// Cricket scoring engine for American, Mickey Mouse and Mouse, for practice
/// (one side) or match play (two sides, alternating turns). Plain C#, no UI types.
///
/// Input is the dart that was hit; the engine decides where it counts:
///   • 15–20 / Bull count on their number (single 1, double 2, treble 3 marks; 25 = 1, DB = 2).
///   • Mouse: any treble can count on T and any double (incl. DB) on D — 1 mark each;
///     three darts in one bed can count as one 3B mark instead of individually.
///   • A dart only counts in one place. With one live target it goes there; with two,
///     it defaults to the one worth more marks (the number) and the thrower can switch.
///   • A 3B trip is counted individually by default, unless none of its darts would count
///     anywhere, in which case it's 3B automatically; the thrower can switch.
///
/// Scoring: in American / Mouse match play, marks past closing score while the opponent
/// hasn't closed that target — face value on numbers, the whole dart on T / D (T20 = 60),
/// three singles of the bed on 3B. Mickey Mouse and practice: no points.
/// Win: close everything and — when points are played — be level or ahead.
///
/// Committed turns are replayed to rebuild state, so Back can step into a previous turn.
/// </summary>
public sealed class CricketGame
{
    public const string Triples = "T";
    public const string Doubles = "D";
    public const string Bed = "3B";
    public const string Bull = "Bull";

    private static readonly CricketTarget[] StandardTargets =
    [
        new("20", "20", 20), new("19", "19", 19), new("18", "18", 18),
        new("17", "17", 17), new("16", "16", 16), new("15", "15", 15),
        new(Bull, "B", 25)
    ];

    private static readonly CricketTarget[] MouseExtras =
    [
        new(Triples, "T", 0), new(Doubles, "D", 0), new(Bed, "3B", 0)
    ];

    private sealed record Turn(int Side, DartThrow[] Darts, string?[] Choices, bool AsBed, DateTime At);

    private readonly List<Turn> _turns = [];
    private readonly List<DartThrow> _darts = [];
    private readonly List<string?> _choices = [];
    private bool _asBed;
    private readonly CricketSide[] _sides;
    private readonly List<CricketOcheEntry> _practiceLog = [];

    public CricketGame(CricketVariant variant, IReadOnlyList<string> sideNames)
    {
        if (variant == CricketVariant.English)
            throw new ArgumentException("English cricket uses its own engine.", nameof(variant));
        if (sideNames.Count is < 1 or > 2)
            throw new ArgumentException("Cricket supports one or two sides.", nameof(sideNames));

        Variant = variant;
        Targets = variant == CricketVariant.Mouse ? StandardTargets.Concat(MouseExtras).ToArray() : StandardTargets;
        _sides = sideNames.Select((name, i) => new CricketSide(i, name, Targets)).ToArray();
    }

    public CricketVariant Variant { get; }

    public IReadOnlyList<CricketTarget> Targets { get; }

    public DateTime StartedAt { get; } = DateTime.Now;

    public bool IsPractice => _sides.Length == 1;

    /// <summary>Points are played in American and Mouse match play only.</summary>
    public bool ScoresPoints => !IsPractice && Variant != CricketVariant.MickeyMouse;

    public IReadOnlyList<CricketSide> Sides => _sides;

    public int CurrentSideIndex { get; private set; }

    public CricketSide CurrentSide => _sides[CurrentSideIndex];

    public CricketSide? Opponent(CricketSide side) =>
        _sides.Length == 2 ? _sides[1 - side.Index] : null;

    /// <summary>Darts entered this turn.</summary>
    public IReadOnlyList<DartThrow> CurrentDarts => _darts;

    /// <summary>How this turn's darts count so far.</summary>
    public CricketTurnPlan Live => Plan(CurrentSide, _darts, _choices, _asBed, commit: false);

    public CricketSide? Winner { get; private set; }

    public bool IsOver => Winner is not null;

    public bool HasStarted => _turns.Count > 0 || _darts.Count > 0;

    public bool CanAddDart => !IsOver && _darts.Count < 3 && !Live.Wins;

    public bool CanEnter => !IsOver && (_darts.Count == 3 || Live.Wins);

    public bool CanUndo => _darts.Count > 0 || _turns.Count > 0;

    /// <summary>
    /// True when a hit on this target would do nothing for the thrower, given the
    /// turn so far (<paramref name="plan"/> = <see cref="Live"/>). The pad dims these;
    /// they can still be entered, since the dart was still thrown.
    /// </summary>
    public bool IsDeadFor(CricketTurnPlan plan, string key) =>
        !IsLiveFor(plan.After, key, Opponent(CurrentSide));

    // ── Actions ──────────────────────────────────────────────────────────────

    public bool AddDart(DartThrow dart)
    {
        if (!CanAddDart) return false;
        _darts.Add(dart);
        _choices.Add(null);
        return true;
    }

    /// <summary>Count dart <paramref name="index"/> of this turn on <paramref name="target"/>.</summary>
    public bool Choose(int index, string target)
    {
        if (IsOver || index < 0 || index >= _darts.Count) return false;
        _choices[index] = target;
        return true;
    }

    /// <summary>Count this three-in-a-bed trip as one 3B mark (true) or dart by dart (false).</summary>
    public void CountAsBed(bool asBed) => _asBed = asBed;

    public bool Enter()
    {
        if (!CanEnter) return false;

        var turn = new Turn(CurrentSideIndex, _darts.ToArray(), _choices.ToArray(), _asBed, DateTime.Now);
        _turns.Add(turn);
        ClearTurn();
        Apply(turn);

        if (!IsOver)
            CurrentSideIndex = (turn.Side + 1) % _sides.Length;
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
            _choices.RemoveAt(_choices.Count - 1);
            _asBed = false;
            return true;
        }

        if (_turns.Count == 0)
            return false;

        var last = _turns[^1];
        _turns.RemoveAt(_turns.Count - 1);
        Replay();

        CurrentSideIndex = last.Side;
        _darts.AddRange(last.Darts);
        _choices.AddRange(last.Choices);
        if (_darts.Count > 0)
        {
            _darts.RemoveAt(_darts.Count - 1);
            _choices.RemoveAt(_choices.Count - 1);
        }
        _asBed = false;
        return true;
    }

    /// <summary>Snapshot of side 0 as a practice session for history.</summary>
    public CricketSession ToPracticeSession()
    {
        var side = _sides[0];
        var session = new CricketSession(Variant)
        {
            StartedAt = StartedAt,
            DartsThrown = side.DartsThrown,
            MarksScored = side.MarksScored,
            OcheCount = side.OcheTrips
        };

        foreach (var (key, marks) in side.Marks)
            session.Marks[key] = marks;

        session.OcheLog.AddRange(_practiceLog);

        if (side.FinishedAt is { } done)
        {
            session.CompletedAt = done;
            session.IsComplete = true;
        }

        return session;
    }

    // ── Routing and scoring ──────────────────────────────────────────────────

    /// <summary>Every target a dart could count on, number target first.</summary>
    private IEnumerable<string> TargetsFor(DartThrow dart)
    {
        if (dart.Number is >= 15 and <= 20) yield return dart.Number.ToString();
        if (dart.Number == 25) yield return Bull;
        if (Variant != CricketVariant.Mouse || dart.Number == 0) yield break;
        if (dart.Multiplier == 3) yield return Triples;
        if (dart.Multiplier == 2) yield return Doubles;
    }

    private static int MarksFor(DartThrow dart, string key) =>
        key is Triples or Doubles ? 1 : dart.Multiplier;

    private static int PointsPerMark(DartThrow dart, string key) =>
        key is Triples or Doubles ? dart.Score : dart.Number;

    private bool CanScoreOn(string key, CricketSide? opponent) =>
        ScoresPoints && opponent is not null && !opponent.IsClosed(key);

    private bool IsLiveFor(CricketSide state, string key, CricketSide? opponent) =>
        !state.IsClosed(key) || CanScoreOn(key, opponent);

    /// <summary>Put marks on a target; returns the marks counted.</summary>
    private int Place(CricketSide state, string key, int marks, int pointsPerMark, CricketSide? opponent)
    {
        var have = state.Marks[key];
        var closing = Math.Min(Math.Max(0, 3 - have), marks);
        var extra = marks - closing;

        state.SetMarks(key, have + closing);
        state.MarksScored += marks;

        if (extra > 0 && CanScoreOn(key, opponent))
            state.Points += extra * pointsPerMark;

        return marks;
    }

    private bool HasWon(CricketSide side)
    {
        if (!side.AllClosed) return false;
        var opponent = Opponent(side);
        return opponent is null || !ScoresPoints || side.Points >= opponent.Points;
    }

    /// <summary>
    /// Work out how a turn's darts count. With <paramref name="commit"/> the side itself
    /// is updated; otherwise a copy is used for the live preview.
    /// </summary>
    private CricketTurnPlan Plan(CricketSide side, IReadOnlyList<DartThrow> darts,
                                 IReadOnlyList<string?> choices, bool asBed, bool commit)
    {
        var state = commit ? side : side.Clone();
        var opponent = Opponent(side);
        var placements = new List<CricketPlacement>();

        // Three in a bed?
        var bedAvailable = Variant == CricketVariant.Mouse
                           && darts.Count == 3
                           && darts[0].Number != 0
                           && darts.All(d => d.Number == darts[0].Number)
                           && IsLiveFor(state, Bed, opponent);
        var individualUseful = darts.Any(d => TargetsFor(d).Any(k => IsLiveFor(state, k, opponent)));
        var bedIsChoice = bedAvailable && individualUseful;

        if (bedAvailable && (asBed || !individualUseful))
        {
            var number = darts[0].Number;
            var marks = Place(state, Bed, 1, number * 3, opponent);
            placements.AddRange(darts.Select((d, i) => new CricketPlacement(d, [Bed], Bed, i == 0 ? marks : 0)));
            return new CricketTurnPlan(placements, true, bedIsChoice, true, marks, HasWon(state), state);
        }

        var total = 0;
        var wins = false;
        for (var i = 0; i < darts.Count; i++)
        {
            var dart = darts[i];
            var options = TargetsFor(dart).Where(k => IsLiveFor(state, k, opponent)).ToList();

            string? target = null;
            var marks = 0;
            if (options.Count > 0)
            {
                // Chosen target if still valid, else the one worth most marks (number first on ties).
                target = choices[i] is { } chosen && options.Contains(chosen)
                    ? chosen
                    : options.OrderByDescending(k => MarksFor(dart, k)).First();
                marks = Place(state, target, MarksFor(dart, target), PointsPerMark(dart, target), opponent);
            }

            placements.Add(new CricketPlacement(dart, options, target, marks));
            total += marks;

            if (HasWon(state))
            {
                wins = true;
                break;   // darts after a winning dart don't count
            }
        }

        return new CricketTurnPlan(placements, bedAvailable, bedIsChoice, false, total, wins, state);
    }

    private void Apply(Turn turn)
    {
        var side = _sides[turn.Side];
        side.OcheTrips++;

        var plan = Plan(side, turn.Darts, turn.Choices, turn.AsBed, commit: true);
        side.DartsThrown += plan.Darts.Count;

        if (turn.Side == 0)
        {
            foreach (var p in plan.Darts.Where(p => p.Target is not null && p.Marks > 0))
                _practiceLog.Add(new CricketOcheEntry(side.OcheTrips, p.Target!, p.Marks));
        }

        if (plan.Wins)
        {
            side.FinishedAt = turn.At;
            Winner = side;
        }
    }

    private void ClearTurn()
    {
        _darts.Clear();
        _choices.Clear();
        _asBed = false;
    }

    private void Replay()
    {
        foreach (var side in _sides)
            side.Reset();
        _practiceLog.Clear();

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
