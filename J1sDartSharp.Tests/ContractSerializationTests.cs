using System.Text.Json;
using J1sDartSharp.Shared.Contracts;

namespace J1sDartSharp.Tests;

/// <summary>
/// Confirms the wire format the Api and the MAUI client will share:
/// enums as names, DateOnly as yyyy-MM-dd, and clean round-trips —
/// using default serializer options, i.e. no per-side configuration.
/// </summary>
public class ContractSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Enums_serialize_as_names()
    {
        var json = JsonSerializer.Serialize(new PlayerDto(1, "Linda", Gender.Female, 3, true), Web);

        Assert.Contains("\"gender\":\"Female\"", json);
    }

    [Fact]
    public void DateOnly_serializes_as_iso_date()
    {
        var dto = new MatchSummaryDto(1, "Diddler & Co.", new DateOnly(2026, 4, 21), "Crown & Anchor",
                                      MatchStatus.Draft, 0, 11, 0);

        var json = JsonSerializer.Serialize(dto, Web);

        Assert.Contains("\"matchDate\":\"2026-04-21\"", json);
        Assert.Contains("\"status\":\"Draft\"", json);
    }

    [Fact]
    public void Match_detail_round_trips()
    {
        var linda = new PlayerRefDto(1, "Linda", Gender.Female);
        var dto = new MatchDetailDto(
            7, "It's Irrelevant", new DateOnly(2026, 4, 14), "Top Spin", MatchStatus.Finalized,
            [new GameDto(70, 1, GameType.SinglesCricket, GameFormat.Singles, "Singles Cricket", 1,
                         GameStatus.Completed, 1, 0, [linda])],
            [new AbsenceDto(3, new PlayerRefDto(4, "Dave", Gender.Male), "Work")],
            [new PlayerLoadDto(linda, 1)]);

        var json = JsonSerializer.Serialize(dto, Web);
        var back = JsonSerializer.Deserialize<MatchDetailDto>(json, Web)!;

        Assert.Equal(dto.Opponent, back.Opponent);
        Assert.Equal(dto.MatchDate, back.MatchDate);
        Assert.Equal(GameType.SinglesCricket, back.Games[0].GameType);
        Assert.Equal("Linda", back.Games[0].Players[0].Name);
        Assert.Equal("Dave", back.Absences[0].Player.Name);
        Assert.Equal(1, back.Load[0].Games);
    }

    [Fact]
    public void Enum_names_are_accepted_case_insensitively_on_input()
    {
        var request = JsonSerializer.Deserialize<SavePlayerRequest>(
            """{"name":"Pam","gender":"female","rank":2}""", Web)!;

        Assert.Equal(Gender.Female, request.Gender);
    }
}
