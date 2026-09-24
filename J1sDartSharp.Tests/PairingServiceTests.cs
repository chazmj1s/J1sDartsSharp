using J1sDartSharp.Core.Models;
using J1sDartSharp.Core.Services;

namespace J1sDartSharp.Tests;

/// <summary>
/// Checks the PairingService port against the hard league rules for every
/// legal roster size, plus a golden test that pins the exact schedule for the
/// seeds.rb roster. The rules marked "RULES TO REVISIT" in PairingService
/// (female singles/doubles coverage) are deliberately NOT asserted here.
/// </summary>
public class PairingServiceTests
{
    // seeds.rb roster first (Ids 1–6), then two extra players for 7- and 8-player runs.
    private static readonly Player[] FullRoster =
    [
        NewPlayer(1, "Linda",   Gender.Female, 3),
        NewPlayer(2, "Anita",   Gender.Female, 3),
        NewPlayer(3, "Mike",    Gender.Male,   1),
        NewPlayer(4, "Dave",    Gender.Male,   1),
        NewPlayer(5, "Charlie", Gender.Male,   2),
        NewPlayer(6, "Ronnie",  Gender.Male,   2),
        NewPlayer(7, "Pam",     Gender.Female, 2),
        NewPlayer(8, "Zed",     Gender.Male,   3),
    ];

    public static TheoryData<int> RosterSizes => new() { 4, 5, 6, 7, 8 };

    // ── Hard rules, every roster size ─────────────────────────────────────

    [Theory]
    [MemberData(nameof(RosterSizes))]
    public void Every_game_gets_the_right_number_of_distinct_players(int rosterSize)
    {
        var result = Run(rosterSize);

        Assert.Equal(LeagueRules.GamesPerMatch, result.Assignments.Count);

        foreach (var a in result.Assignments)
        {
            Assert.Equal(a.Game.PlayerCount, a.Players.Count);
            Assert.Equal(a.Players.Count, a.Players.Select(p => p.Id).Distinct().Count());
        }

        Assert.Equal(LeagueRules.TotalSlots, result.Assignments.Sum(a => a.Players.Count));
    }

    [Theory]
    [MemberData(nameof(RosterSizes))]
    public void Games_are_returned_in_sequence_order(int rosterSize)
    {
        var sequences = Run(rosterSize).Assignments.Select(a => a.Game.Sequence).ToList();

        Assert.Equal(Enumerable.Range(1, LeagueRules.GamesPerMatch), sequences);
    }

    [Theory]
    [MemberData(nameof(RosterSizes))]
    public void Load_is_balanced_within_one_game(int rosterSize)
    {
        var load = Run(rosterSize).LoadByPlayerId;

        Assert.Equal(rosterSize, load.Count);
        Assert.Equal(LeagueRules.TotalSlots, load.Values.Sum());
        Assert.True(load.Values.Max() - load.Values.Min() <= 1,
            $"Load spread too wide: {string.Join(", ", load.Values)}");
    }

    [Theory]
    [MemberData(nameof(RosterSizes))]
    public void No_player_plays_both_games_of_the_same_type(int rosterSize)
    {
        var byType = Run(rosterSize).Assignments.GroupBy(a => a.Game.GameType);

        foreach (var group in byType)
        {
            var ids = group.SelectMany(a => a.Players).Select(p => p.Id).ToList();
            Assert.True(ids.Count == ids.Distinct().Count(),
                $"{group.Key}: a player appears in both games.");
        }
    }

    [Theory]
    [MemberData(nameof(RosterSizes))]
    public void No_doubles_partnership_repeats(int rosterSize)
    {
        var pairs = Run(rosterSize).Assignments
            .Where(a => a.Game.Format == GameFormat.Doubles)
            .Select(a => a.Players.Select(p => p.Id).Order().ToArray())
            .Select(ids => (ids[0], ids[1]))
            .ToList();

        Assert.Equal(LeagueRules.DoublesGameCount, pairs.Count);
        Assert.Equal(pairs.Count, pairs.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(RosterSizes))]
    public void Team_game_includes_a_woman(int rosterSize)
    {
        var team = Run(rosterSize).Assignments.Single(a => a.Game.Format == GameFormat.Team);

        Assert.Contains(team.Players, p => p.IsFemale);
    }

    [Fact]
    public void Same_input_gives_the_same_schedule()
    {
        var first = Describe(Run(6));
        var second = Describe(Run(6));

        Assert.Equal(first, second);
    }

    // ── Validation ────────────────────────────────────────────────────────

    [Fact]
    public void Fewer_than_four_players_throws_insufficient_players()
    {
        var ex = Assert.Throws<InsufficientPlayersException>(() => Run(3));

        Assert.Contains("minimum is 4", ex.Message);
    }

    [Fact]
    public void No_women_available_throws_insufficient_players()
    {
        var menOnly = FullRoster.Where(p => p.IsMale).Take(4).ToList();

        Assert.Throws<InsufficientPlayersException>(
            () => new PairingService().BuildSchedule(menOnly, Slate()));
    }

    [Fact]
    public void Unsaved_players_are_rejected()
    {
        var roster = Roster(4);
        roster.Add(NewPlayer(0, "Unsaved", Gender.Male, 2));

        Assert.Throws<ArgumentException>(() => new PairingService().BuildSchedule(roster, Slate()));
    }

    // ── Golden: exact schedule for the seeds.rb roster ────────────────────
    // Pins current behavior so any change to the port shows up immediately.
    // If you change the rules on purpose, update this list.

    [Fact]
    public void Seed_roster_produces_the_expected_schedule()
    {
        string[] expected =
        [
            "1 SinglesCricket: Anita",
            "2 SinglesCricket: Dave",
            "3 Singles501: Mike",
            "4 Singles501: Charlie",
            "5 DoublesChicago: Linda, Ronnie",
            "6 DoublesChicago: Dave, Mike",
            "7 DoublesCricket: Charlie, Ronnie",
            "8 DoublesCricket: Anita, Linda",
            "9 Doubles501: Charlie, Dave",
            "10 Doubles501: Mike, Ronnie",
            "11 Team4Player: Anita, Linda, Dave, Mike",
        ];

        var result = Run(6);

        Assert.Equal(expected, Describe(result));

        var loadByName = FullRoster.Take(6).ToDictionary(p => p.Name, p => result.LoadByPlayerId[p.Id]);
        Assert.Equal(4, loadByName["Dave"]);
        Assert.Equal(4, loadByName["Mike"]);
        Assert.All(new[] { "Linda", "Anita", "Charlie", "Ronnie" }, n => Assert.Equal(3, loadByName[n]));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Player NewPlayer(int id, string name, Gender gender, int rank) =>
        new() { Id = id, Name = name, Gender = gender, Rank = rank };

    private static List<Player> Roster(int size) => FullRoster.Take(size).ToList();

    private static List<Game> Slate() =>
        Match.CreateWithStandardSlate("Test Opponent", new DateOnly(2026, 4, 7)).Games.ToList();

    private static PairingResult Run(int rosterSize) =>
        new PairingService().BuildSchedule(Roster(rosterSize), Slate());

    private static string[] Describe(PairingResult result) =>
        result.Assignments
            .Select(a => $"{a.Game.Sequence} {a.Game.GameType}: {string.Join(", ", a.Players.Select(p => p.Name))}")
            .ToArray();
}
