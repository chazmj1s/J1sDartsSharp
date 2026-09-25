namespace J1sDartSharp.Models;

// ── Shared ──────────────────────────────────────────────────────────────────

/// <summary>
/// Practice game types. EightOhOne exists for match play only (match play is
/// a scoreboard and is never saved), so it never appears in saved history.
/// </summary>
public enum GameType { Cricket, ThreeOhOne, FiveOhOne, EightOhOne }

/// <summary>Base record saved to history for every session.</summary>
public abstract class SessionBase
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public GameType Game { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt - StartedAt : null;
    public bool IsComplete { get; set; }
}

// ── Cricket ─────────────────────────────────────────────────────────────────

/// <summary>
/// One saved practice game of cricket (American, Mickey Mouse or Mouse).
/// Targets: 15–20 and Bull; Mouse adds T, D and 3B.
/// A target is "closed" once it has 3 marks.
/// A trip to the oche = one visit (up to 3 darts).
/// </summary>
public class CricketSession : SessionBase
{
    public CricketSession() : this(CricketVariant.American) { }

    public CricketSession(CricketVariant variant)
    {
        Game = GameType.Cricket;
        Variant = variant;

        foreach (var key in new[] { "15", "16", "17", "18", "19", "20", "Bull" })
            Marks[key] = 0;

        if (variant == CricketVariant.Mouse)
        {
            Marks[CricketGame.Triples] = 0;
            Marks[CricketGame.Doubles] = 0;
            Marks[CricketGame.Bed] = 0;
        }
    }

    public CricketVariant Variant { get; init; }

    /// <summary>Marks toward closing each target (0–3).</summary>
    public Dictionary<string, int> Marks { get; set; } = new();

    /// <summary>Total trips to the oche this session.</summary>
    public int OcheCount { get; set; } = 0;

    public int DartsThrown { get; set; }

    /// <summary>All marks thrown on targets, including scoring marks after closing.</summary>
    public int MarksScored { get; set; }

    /// <summary>Marks thrown (falls back to closing marks for older sessions).</summary>
    public int TotalMarks => MarksScored > 0 ? MarksScored : Marks.Values.Sum();

    /// <summary>Marks per round.</summary>
    public double Mpr => OcheCount > 0 ? (double)TotalMarks / OcheCount : 0;

    /// <summary>Targets that are closed (3+ marks).</summary>
    public int ClosedTargets => Marks.Count(kvp => kvp.Value >= 3);

    public bool AllClosed => Marks.Values.All(v => v >= 3);

    public string VariantLabel => Variant switch
    {
        CricketVariant.MickeyMouse => "Mickey Mouse",
        CricketVariant.Mouse => "Mouse",
        CricketVariant.English => "English",
        _ => "American"
    };

    // Per-oche log for history / sparkline
    public List<CricketOcheEntry> OcheLog { get; set; } = new();
}

public record CricketOcheEntry(int OcheNumber, string Target, int MarksScored);

// ── X01 (301 / 501 / 801) ────────────────────────────────────────────────────

public enum InMode { SingleIn, DoubleIn }

public enum OutMode { SingleOut, DoubleOut }

/// <summary>
/// One saved practice leg of 301 or 501. Built from the finished
/// <see cref="X01Game"/> (side 0) or rehydrated from a history row.
/// </summary>
public class X01Session : SessionBase
{
    public X01Session(int startScore, InMode inMode = InMode.DoubleIn, OutMode outMode = OutMode.DoubleOut)
    {
        StartScore = startScore;
        Game = GameTypeFor(startScore);
        InMode = inMode;
        OutMode = outMode;
        RemainingScore = startScore;

        // Single in: already "in" at the start
        if (inMode == InMode.SingleIn)
        {
            DoubleInAchieved = true;
            DoubleInAt = DateTime.Now;
        }
    }

    public static GameType GameTypeFor(int startScore) => startScore switch
    {
        301 => GameType.ThreeOhOne,
        801 => GameType.EightOhOne,
        _ => GameType.FiveOhOne
    };

    public int StartScore { get; init; }
    public InMode InMode { get; init; }
    public OutMode OutMode { get; init; }

    /// <summary>"SIDO", "DIDO", "SISO" or "DISO".</summary>
    public string ModeTag =>
        $"{(InMode == InMode.SingleIn ? 'S' : 'D')}I{(OutMode == OutMode.SingleOut ? 'S' : 'D')}O";

    // ── Double-in tracking ───────────────────────────────────────────────────
    public int DartsToDoubleIn { get; set; } = 0;
    public bool DoubleInAchieved { get; set; } = false;
    public DateTime? DoubleInAt { get; set; }
    public TimeSpan? TimeToDoubleIn => DoubleInAt.HasValue ? DoubleInAt - StartedAt : null;

    // ── Scoring ──────────────────────────────────────────────────────────────
    public int RemainingScore { get; set; }

    /// <summary>Every trip to the oche, including busts and trips before the double-in.</summary>
    public List<X01OcheEntry> OcheLog { get; set; } = new();

    /// <summary>Every dart thrown, including misses and double-in attempts.</summary>
    public int DartsThrown { get; set; }

    public int TotalDarts => DartsThrown;

    public int OcheTrips => OcheLog.Count > 0 ? OcheLog.Count : StoredOcheTrips ?? 0;

    public int PointsScored => StartScore - RemainingScore;

    /// <summary>Three-dart average: points scored per 3 darts thrown.</summary>
    public double AveragePerOche =>
        StoredAverage ?? (DartsThrown > 0 ? PointsScored * 3.0 / DartsThrown : 0);

    public double ThreeDartAverage => AveragePerOche;

    /// <summary>Set only when rehydrated from a history row (no per-trip log is loaded).</summary>
    public double? StoredAverage { get; set; }

    /// <summary>Set only when rehydrated from a history row (no per-trip log is loaded).</summary>
    public int? StoredOcheTrips { get; set; }

    // ── Check-out tracking ───────────────────────────────────────────────────
    // "DoubleOut" names are kept for the saved columns; they mean "checked out"
    // and are also set for single-out finishes.
    public bool DoubleOutAchieved { get; set; } = false;
    public DateTime? DoubleOutAt { get; set; }
    public int FinishingDart { get; set; } = 0;  // 1, 2, or 3 — which dart in the trip finished

    public TimeSpan? TimeToDoubleOut =>
        DoubleOutAt.HasValue && DoubleInAt.HasValue
            ? DoubleOutAt - DoubleInAt
            : null;

    // ── Bust tracking ────────────────────────────────────────────────────────
    public int BustCount { get; set; } = 0;
}

/// <summary>One trip to the oche. A bust scores 0 and leaves the remaining score unchanged.</summary>
public record X01OcheEntry(int OcheNumber, int Score, int RemainingAfter, bool IsBust = false, int Darts = 3);
