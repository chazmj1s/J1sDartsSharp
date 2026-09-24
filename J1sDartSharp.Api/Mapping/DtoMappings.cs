using J1sDartSharp.Core.Models;
using J1sDartSharp.Shared.Contracts;

namespace J1sDartSharp.Api.Mapping;

/// <summary>
/// Entity → DTO conversions. Hand-written on purpose: the shapes are small,
/// and explicit mapping keeps the wire contract from drifting when an
/// entity gains a column.
/// </summary>
public static class DtoMappings
{
    // ── Players ──────────────────────────────────────────────────────────

    public static PlayerDto ToDto(this Player player) =>
        new(player.Id, player.Name, player.Gender, player.Rank, player.Active);

    public static PlayerRefDto ToRef(this Player player) =>
        new(player.Id, player.Name, player.Gender);

    // ── Games / absences ─────────────────────────────────────────────────
    // Require GameParticipants.Player / Absence.Player to be loaded.

    public static GameDto ToDto(this Game game) =>
        new(game.Id,
            game.Sequence,
            game.GameType,
            game.Format,
            game.DisplayName,
            game.PlayerCount,
            game.Status,
            game.HomeScore,
            game.AwayScore,
            game.GameParticipants
                .Where(gp => gp.Side == ParticipantSide.Home)   // Rails: home_players
                .OrderBy(gp => gp.Id)
                .Select(gp => gp.Player.ToRef())
                .ToList());

    public static AbsenceDto ToDto(this Absence absence) =>
        new(absence.Id, absence.Player.ToRef(), absence.Reason);

    // ── Matches ──────────────────────────────────────────────────────────

    /// <summary>
    /// Rails: matches#show. Requires Games → GameParticipants → Player and
    /// Absences → Player to be loaded. <paramref name="availablePlayers"/> is
    /// Player.available_for(match) and drives the load report.
    /// </summary>
    public static MatchDetailDto ToDetail(this Match match, IReadOnlyList<Player> availablePlayers)
    {
        var games = match.Games.OrderBy(g => g.Sequence).ToList();

        // Rails: load_report — games in this match per available player.
        var load = availablePlayers
            .Select(p => new PlayerLoadDto(
                p.ToRef(),
                games.Count(g => g.GameParticipants.Any(gp => gp.PlayerId == p.Id))))
            .ToList();

        return new MatchDetailDto(
            match.Id,
            match.Opponent,
            match.MatchDate,
            match.Location,
            match.Status,
            games.Select(g => g.ToDto()).ToList(),
            match.Absences.OrderBy(a => a.Player.Name).Select(a => a.ToDto()).ToList(),
            load);
    }
}
