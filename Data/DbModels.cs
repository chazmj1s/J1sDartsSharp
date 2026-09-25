using SQLite;

namespace J1sDartSharp.Data;

/// <summary>
/// SQLite row for every completed or abandoned session.
/// Stores the key stats as flat columns; full dart-level detail goes in DartLogRow.
///
/// Several properties were renamed from "Ochre" to "Oche". Their [Column]
/// attributes keep the original column names so existing databases still load.
/// Don't remove those attributes.
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
    [Column("CricketOchres")]
    public int? CricketOches      { get; set; }
    public int? CricketTotalMarks { get; set; }

    /// <summary>"American" | "MickeyMouse" | "Mouse". Null on older rows (American).</summary>
    public string? CricketVariant      { get; set; }
    public int?    CricketDarts        { get; set; }

    // ── X01 ──────────────────────────────────────────────────────────────────
    public int?      X01StartScore        { get; set; }

    /// <summary>"SingleIn" | "DoubleIn". Null on rows saved before in/out options existed.</summary>
    public string?   X01InMode            { get; set; }

    /// <summary>"SingleOut" | "DoubleOut". Null on older rows (all were double out).</summary>
    public string?   X01OutMode           { get; set; }

    public int?      X01DartsToDoubleIn   { get; set; }
    public DateTime? X01DoubleInAt        { get; set; }

    /// <summary>Three-dart average.</summary>
    [Column("X01AvgPerOchre")]
    public double?   X01AvgPerOche        { get; set; }

    /// <summary>Trips to the oche (older rows: scoring trips only).</summary>
    [Column("X01ScoringOchres")]
    public int?      X01OcheTrips         { get; set; }

    /// <summary>True when the leg was checked out (single or double out).</summary>
    public bool?     X01DoubleOutAchieved { get; set; }
    public DateTime? X01DoubleOutAt       { get; set; }
    public int?      X01FinishingDart     { get; set; }
    public int?      X01TotalDarts        { get; set; }
    public int?      X01BustCount         { get; set; }
}

/// <summary>One dart (or, for X01, one trip total) keyed to a session.</summary>
[Table("dart_log")]
public class DartLogRow
{
    [PrimaryKey, AutoIncrement]
    public int RowId { get; set; }

    [Indexed]
    public string SessionId { get; set; } = string.Empty;

    [Column("OchreNumber")]
    public int OcheNumber { get; set; }

    [Column("DartInOchre")]
    public int DartInOche { get; set; }   // 1, 2, or 3

    /// <summary>0 = miss, 1-20 = segment, 25 = bull</summary>
    public int Number { get; set; }

    /// <summary>1 = single, 2 = double, 3 = triple</summary>
    public int Multiplier { get; set; }

    public int Score => Number * Multiplier;

    /// <summary>For cricket: which target key was hit (e.g. "20", "Bull"). Null for X01.</summary>
    public string? CricketTarget { get; set; }
}
