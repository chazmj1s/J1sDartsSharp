using System.Text.Json.Serialization;

namespace J1sDartSharp.Shared.Enums;

// Rails stored all of these as free strings validated with `inclusion:`.
// Enums make invalid states unrepresentable; the API's DbContext persists
// them as their string names so the database stays human-readable.
//
// [JsonConverter] makes every enum serialize as its name ("Female", not 1)
// over the API — on both the server and the MAUI client — with no extra
// JsonSerializerOptions setup on either side.

[JsonConverter(typeof(JsonStringEnumConverter<Gender>))]
public enum Gender
{
    Male,
    Female
}

[JsonConverter(typeof(JsonStringEnumConverter<GameType>))]
public enum GameType
{
    SinglesCricket,
    Singles501,
    DoublesChicago,
    DoublesCricket,
    Doubles501,
    Team4Player
}

[JsonConverter(typeof(JsonStringEnumConverter<GameFormat>))]
public enum GameFormat
{
    Singles,
    Doubles,
    Team
}

[JsonConverter(typeof(JsonStringEnumConverter<GameStatus>))]
public enum GameStatus
{
    Unassigned,
    Assigned,
    Completed
}

[JsonConverter(typeof(JsonStringEnumConverter<MatchStatus>))]
public enum MatchStatus
{
    Draft,
    Finalized,
    Completed
}

[JsonConverter(typeof(JsonStringEnumConverter<ParticipantSide>))]
public enum ParticipantSide
{
    Home,
    Away
}

/// <summary>
/// Rails only ever wrote "player". Kept as an enum so a future
/// "substitute" (or similar) role is a one-line addition.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParticipantRole>))]
public enum ParticipantRole
{
    Player
}
