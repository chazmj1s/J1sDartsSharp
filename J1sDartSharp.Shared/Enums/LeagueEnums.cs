namespace J1sDartSharp.Shared.Enums;

// Rails stored all of these as free strings validated with `inclusion:`.
// Enums make invalid states unrepresentable; the API's DbContext persists
// them as their string names so the database stays human-readable.

public enum Gender
{
    Male,
    Female
}

public enum GameType
{
    SinglesCricket,
    Singles501,
    DoublesChicago,
    DoublesCricket,
    Doubles501,
    Team4Player
}

public enum GameFormat
{
    Singles,
    Doubles,
    Team
}

public enum GameStatus
{
    Unassigned,
    Assigned,
    Completed
}

public enum MatchStatus
{
    Draft,
    Finalized,
    Completed
}

public enum ParticipantSide
{
    Home,
    Away
}

/// <summary>
/// Rails only ever wrote "player". Kept as an enum so a future
/// "substitute" (or similar) role is a one-line addition.
/// </summary>
public enum ParticipantRole
{
    Player
}
