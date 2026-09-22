using J1sDartSharp.Shared.Enums;

namespace J1sDartSharp.Shared.Constants;

/// <summary>
/// Official league rules and slate definition.
/// Consolidates Rails' Match::STANDARD_GAMES, Game::GAME_TYPES/FORMATS,
/// Player::GENDERS/MIN_ROSTER/MAX_ROSTER and PairingService's constants.
/// </summary>
public static class LeagueRules
{
    // ── Roster ───────────────────────────────────────────────────────────
    public const int MinRoster = 4;
    public const int MaxRoster = 8;
    public const int MinRank = 1;
    public const int MaxRank = 3;

    // ── Slate ────────────────────────────────────────────────────────────
    public const int GamesPerMatch = 11;
    public const int SinglesGameCount = 4;
    public const int DoublesGameCount = 6;
    public const int TeamSize = 4;

    /// <summary>4×1 + 6×2 + 1×4 = 20 player-game slots per match.</summary>
    public const int TotalSlots = 20;

    /// <summary>
    /// The standard 11-game slate, in sequence order (index 0 = game 1).
    /// </summary>
    public static readonly IReadOnlyList<GameType> StandardGames =
    [
        GameType.SinglesCricket,
        GameType.SinglesCricket,
        GameType.Singles501,
        GameType.Singles501,
        GameType.DoublesChicago,
        GameType.DoublesChicago,
        GameType.DoublesCricket,
        GameType.DoublesCricket,
        GameType.Doubles501,
        GameType.Doubles501,
        GameType.Team4Player
    ];

    public static GameFormat FormatOf(GameType type) => type switch
    {
        GameType.SinglesCricket or GameType.Singles501 => GameFormat.Singles,
        GameType.DoublesChicago or GameType.DoublesCricket or GameType.Doubles501 => GameFormat.Doubles,
        GameType.Team4Player => GameFormat.Team,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown game type.")
    };

    /// <summary>
    /// All game types belonging to a format. Useful for EF queries
    /// (translates to SQL IN) since Format is not a stored column.
    /// </summary>
    public static IReadOnlyList<GameType> TypesOf(GameFormat format) =>
        Enum.GetValues<GameType>().Where(t => FormatOf(t) == format).ToArray();

    public static int PlayerCountFor(GameFormat format) => format switch
    {
        GameFormat.Singles => 1,
        GameFormat.Doubles => 2,
        GameFormat.Team => TeamSize,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format.")
    };

    public static string DisplayNameOf(GameType type) => type switch
    {
        GameType.SinglesCricket => "Singles Cricket",
        GameType.Singles501 => "Singles 501",
        GameType.DoublesChicago => "Doubles Chicago",
        GameType.DoublesCricket => "Doubles Cricket",
        GameType.Doubles501 => "Doubles 501",
        GameType.Team4Player => "Team (4-Player)",
        _ => type.ToString()
    };
}
