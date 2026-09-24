using J1sDartSharp.Api.Data;
using J1sDartSharp.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace J1sDartSharp.Api.Controllers;

/// <summary>
/// Port of app/controllers/games_controller.rb.
///
/// Rails → API:
///   update    PUT  /api/matches/{matchId}/games/{gameId}           → MatchDetailDto
///   complete  POST /api/matches/{matchId}/games/{gameId}/complete  → MatchDetailDto
///
/// Differences from Rails:
///   • Editing players on a finalized/completed match returns 409 (Rails hid
///     the edit form instead).
///   • Selected players must be available for the match (active, not absent).
///     Rails didn't check; the form only offered available players anyway.
/// Rule failures return 422 with the Rails message.
/// </summary>
[ApiController]
[Route("api/matches/{matchId:int}/games")]
public sealed class GamesController(LeagueDbContext db) : ControllerBase
{
    // PUT /api/matches/{matchId}/games/{gameId}
    // Rails: manual override of player assignments for one game.
    [HttpPut("{gameId:int}")]
    public async Task<ActionResult<MatchDetailDto>> UpdatePlayers(
        int matchId, int gameId, UpdateGamePlayersRequest request, CancellationToken ct)
    {
        var match = await db.Matches
            .Include(m => m.Games).ThenInclude(g => g.GameParticipants).ThenInclude(gp => gp.Player)
            .Include(m => m.Absences)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);

        var game = match?.Games.FirstOrDefault(g => g.Id == gameId);
        if (match is null || game is null)
            return NotFound();

        if (match.IsFinalized || match.IsCompleted)
            return Problem(
                title: "Lineup is locked",
                detail: $"This match is {match.Status.ToString().ToLowerInvariant()}; players can't be changed.",
                statusCode: StatusCodes.Status409Conflict);

        // Rails: player_ids.map(&:to_i).compact.uniq
        var ids = request.PlayerIds.Distinct().ToList();

        // Rails: validate_player_count!
        if (ids.Count != game.PlayerCount)
            return RuleViolation($"This game needs {game.PlayerCount} player(s); you selected {ids.Count}.");

        var available = await db.GetAvailablePlayersAsync(
            match.Absences.Select(a => a.PlayerId), tracking: false, ct);

        var selected = available.Where(p => ids.Contains(p.Id)).ToList();
        if (selected.Count != ids.Count)
            return RuleViolation("One or more selected players are not available for this match.");

        // Rails: validate_female_coverage! — only when a woman is available.
        // Passes if this game has a woman, or another ASSIGNED game of the
        // same format already does.
        if (available.Any(p => p.IsFemale) && !selected.Any(p => p.IsFemale))
        {
            var femaleElsewhere = match.Games
                .Where(g => g.Id != game.Id && g.Format == game.Format && g.Status == GameStatus.Assigned)
                .Any(g => g.GameParticipants.Any(gp => gp.Player.IsFemale));

            if (!femaleElsewhere)
            {
                var format = game.Format.ToString().ToLowerInvariant();
                return RuleViolation(
                    $"At least one female player must appear in a {format} game. " +
                    $"Please include a female player here or ensure another {format} game has one.");
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Deletes saved before inserts so re-selecting the same player can't
        // trip the (GameId, PlayerId) unique index.
        db.GameParticipants.RemoveRange(game.GameParticipants);
        await db.SaveChangesAsync(ct);

        foreach (var player in selected)
        {
            db.GameParticipants.Add(new()
            {
                GameId = game.Id,
                PlayerId = player.Id,
                Side = ParticipantSide.Home,
                Role = ParticipantRole.Player
            });
        }

        game.Status = GameStatus.Assigned;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await db.GetMatchDetailAsync(matchId, ct))!;
    }

    // POST /api/matches/{matchId}/games/{gameId}/complete
    // Rails: record the score. Allowed on finalized matches — that's when
    // games are actually played.
    [HttpPost("{gameId:int}/complete")]
    public async Task<ActionResult<MatchDetailDto>> Complete(
        int matchId, int gameId, CompleteGameRequest request, CancellationToken ct)
    {
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == gameId && g.MatchId == matchId, ct);
        if (game is null)
            return NotFound();

        game.Status = GameStatus.Completed;
        game.HomeScore = request.HomeScore;
        game.AwayScore = request.AwayScore;

        await db.SaveChangesAsync(ct);

        return (await db.GetMatchDetailAsync(matchId, ct))!;
    }

    private ObjectResult RuleViolation(string detail) =>
        Problem(title: "Invalid lineup", detail: detail, statusCode: StatusCodes.Status422UnprocessableEntity);
}
