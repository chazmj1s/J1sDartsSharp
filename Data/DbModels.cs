using SQLite;

namespace J1sDartSharp.Data;

/// <summary>
/// SQLite row for every completed or abandoned session.
/// Stores the key stats as flat columns; full dart-level detail goes in DartLogRow.
/// </summary>
[Table("sessions")]
public class SessionRow
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>"Cricket" | "301" | "501"</summary>
    [Indexed]
    public string Game { get; set; } = string.Empty;

    public DateTime StartedAt  { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsComplete { get; set; }

    // ── Cricket ──────────────────────────────────────────────────────────────
    public int? CricketOchres    { get; set; }
    public int? CricketTotalMarks { get; set; }

    // ── X01 ──────────────────────────────────────────────────────────────────
    public int?      X01StartScore        { get; set; }
    public int?      X01DartsToDoubleIn   { get; set; }
    public DateTime? X01DoubleInAt        { get; set; }
    public double?   X01AvgPerOchre       { get; set; }
    public int?      X01ScoringOchres     { get; set; }
    public bool?     X01DoubleOutAchieved { get; set; }
    public DateTime? X01DoubleOutAt       { get; set; }
    public int?      X01FinishingDart     { get; set; }
    public int?      X01TotalDarts        { get; set; }
    public int?      X01BustCount         { get; set; }
}

/// <summary>One dart thrown, keyed to a session. Allows full replay / sparkline.</summary>
[Table("dart_log")]
public class DartLogRow
{
    [PrimaryKey, AutoIncrement]
    public int RowId { get; set; }

    [Indexed]
    public string SessionId { get; set; } = string.Empty;

    public int OchreNumber { get; set; }
    public int DartInOchre { get; set; }   // 1, 2, or 3

    /// <summary>0 = miss, 1-20 = segment, 25 = bull</summary>
    public int Number { get; set; }

    /// <summary>1 = single, 2 = double, 3 = triple</summary>
    public int Multiplier { get; set; }

    public int Score => Number * Multiplier;

    /// <summary>For cricket: which target key was hit (e.g. "20", "Bull"). Null for X01.</summary>
    public string? CricketTarget { get; set; }
}
