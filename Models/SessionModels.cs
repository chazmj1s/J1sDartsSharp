namespace J1sDartSharp.Models;

// ── Shared ──────────────────────────────────────────────────────────────────

public enum GameType { Cricket, ThreeOhOne, FiveOhOne }

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
/// Cricket targets: 15–20 and Bull.
/// A "mark" = one hit on a target (single=1, double=2, triple=3).
/// A target is "closed" once you have 3+ marks on it.
/// An "oche" = one visit to the oche (3 darts thrown).
/// </summary>
public class CricketSession : SessionBase
{
    public CricketSession() { Game = GameType.Cricket; }

    // Targets: keys are "15","16","17","18","19","20","Bull"
    public Dictionary<string, int> Marks { get; set; } = new()
    {
        ["15"] = 0, ["16"] = 0, ["17"] = 0,
        ["18"] = 0, ["19"] = 0, ["20"] = 0,
        ["Bull"] = 0
    };

    /// <summary>Total ochres (visits) thrown this session.</summary>
    public int OchreCount { get; set; } = 0;

    /// <summary>Running total of all marks thrown across all targets.</summary>
    public int TotalMarks => Marks.Values.Sum();

    /// <summary>Targets that are closed (3+ marks).</summary>
    public int ClosedTargets => Marks.Count(kvp => kvp.Value >= 3);

    public bool AllClosed => ClosedTargets == 7;

    // Per-ochre log for history / sparkline
    public List<CricketOchreEntry> OchreLog { get; set; } = new();
}

public record CricketOchreEntry(int OchreNumber, string Target, int MarksScored);

// ── X01 (301 / 501) ──────────────────────────────────────────────────────────

public enum InMode { SingleIn, DoubleIn }

/// <summary>
/// Tracks a single 301 or 501 leg.
/// </summary>
public class X01Session : SessionBase
{
    public X01Session(GameType game, InMode inMode = InMode.DoubleIn)
    {
        Game = game;
        InMode = inMode;
        StartScore = game == GameType.FiveOhOne ? 501 : 301;
        RemainingScore = StartScore;

        // SIDO — single in, so double-in is already achieved at start
        if (inMode == InMode.SingleIn)
        {
            DoubleInAchieved = true;
            DoubleInAt = DateTime.Now;
        }
    }

    public int StartScore { get; init; }
    public InMode InMode { get; init; }

    // ── Double-in tracking ───────────────────────────────────────────────────
    public int DartsToDoubleIn { get; set; } = 0;
    public bool DoubleInAchieved { get; set; } = false;
    public DateTime? DoubleInAt { get; set; }
    public TimeSpan? TimeToDoubleIn => DoubleInAt.HasValue ? DoubleInAt - StartedAt : null;

    // ── Scoring ──────────────────────────────────────────────────────────────
    public int RemainingScore { get; set; }

    public List<X01OchreEntry> OchreLog { get; set; } = new();
    public int ScoringOchres => OchreLog.Count;

    public double AveragePerOchre =>
        ScoringOchres > 0 ? OchreLog.Average(o => o.Score) : 0;

    public double ThreeDartAverage => AveragePerOchre;

    // ── Double-out tracking ──────────────────────────────────────────────────
    public bool DoubleOutAchieved { get; set; } = false;
    public DateTime? DoubleOutAt { get; set; }
    public int FinishingDart { get; set; } = 0;  // 1, 2, or 3 — which dart in the ochre finished

    /// <summary>Total darts thrown in the scoring portion of the leg.</summary>
    public int TotalDarts => ScoringOchres > 0
        ? ((ScoringOchres - 1) * 3) + FinishingDart
        : 0;

    public TimeSpan? TimeToDoubleOut =>
        DoubleOutAt.HasValue && DoubleInAt.HasValue
            ? DoubleOutAt - DoubleInAt
            : null;

    // ── Bust tracking ────────────────────────────────────────────────────────
    public int BustCount { get; set; } = 0;
}

public record X01OchreEntry(int OchreNumber, int Score, int RemainingAfter);
