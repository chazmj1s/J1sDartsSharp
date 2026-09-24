using J1sDartSharp.Api.Data;
using J1sDartSharp.Api.Services;
using J1sDartSharp.Core.Models;
using J1sDartSharp.Core.Services;
using J1sDartSharp.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace J1sDartSharp.Api.Controllers;

/// <summary>
/// Port of app/controllers/matches_controller.rb.
///
/// Rails → API:
///   index     GET    /api/matches                 → MatchSummaryDto[] (by date)
///   show      GET    /api/matches/{id}            → MatchDetailDto
///   create    POST   /api/matches                 → 201 MatchDetailDto (11-game slate, unassigned)
///   update    PUT    /api/matches/{id}            → MatchDetailDto
///   destroy   DELETE /api/matches/{id}            → 204
///   finalize  POST   /api/matches/{id}/finalize   → MatchDetailDto
///   reassign  POST   /api/matches/{id}/reassign   → MatchDetailDto
///
/// Difference from Rails: reassign refuses a finalized/completed match
/// (409). Rails relied on the view hiding the button; with an API the
/// server is the only guard.
/// </summary>
[ApiController]
[Route("api/matches")]
public sealed class MatchesController(LeagueDbContext db, LineupService lineup) : ControllerBase
{
    // GET /api/matches
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MatchSummaryDto>>> GetAll(CancellationToken ct)
    {
        var matches = await db.Matches
            .AsNoTracking()
            .OrderBy(m => m.MatchDate)
            .Select(m => new MatchSummaryDto(
                m.Id,
                m.Opponent,
                m.MatchDate,
                m.Location,
                m.Status,
                m.Games.Count(g => g.Status == GameStatus.Completed),
                m.Games.Count(g => g.Status == GameStatus.Assigned),
                m.Games.Count(g => g.Status == GameStatus.Unassigned)))
            .ToListAsync(ct);

        // Ok(...) because ActionResult<T>'s implicit conversion doesn't apply
        // when T is an interface (IReadOnlyList).
        return Ok(matches);
    }

    // GET /api/matches/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<MatchDetailDto>> Get(int id, CancellationToken ct)
    {
        var detail = await db.GetMatchDetailAsync(id, ct);
        if (detail is null)
            return NotFound();

        return detail;
    }

    // POST /api/matches
    // Rails: Match.build_standard_slate — games are created unassigned;
    // pairings are assigned separately via reassign.
    [HttpPost]
    public async Task<ActionResult<MatchDetailDto>> Create(SaveMatchRequest request, CancellationToken ct)
    {
        var match = Match.CreateWithStandardSlate(
            request.Opponent.Trim(),
            request.MatchDate!.Value,
            request.Location.Trim());

        db.Matches.Add(match);
        await db.SaveChangesAsync(ct);

        var detail = await db.GetMatchDetailAsync(match.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = match.Id }, detail);
    }

    // PUT /api/matches/{id}
    [HttpPut("{id:int}")]
    public async Task<ActionResult<MatchDetailDto>> Update(int id, SaveMatchRequest request, CancellationToken ct)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (match is null)
            return NotFound();

        match.Opponent = request.Opponent.Trim();
        match.MatchDate = request.MatchDate!.Value;
        match.Location = request.Location.Trim();

        await db.SaveChangesAsync(ct);

        return (await db.GetMatchDetailAsync(id, ct))!;
    }

    // DELETE /api/matches/{id}
    // Games, participants and absences cascade.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (match is null)
            return NotFound();

        db.Matches.Remove(match);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // POST /api/matches/{id}/finalize
    // Rails: "Lineup locked in!"
    [HttpPost("{id:int}/finalize")]
    public async Task<ActionResult<MatchDetailDto>> Finalize(int id, CancellationToken ct)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (match is null)
            return NotFound();

        match.Status = MatchStatus.Finalized;
        await db.SaveChangesAsync(ct);

        return (await db.GetMatchDetailAsync(id, ct))!;
    }

    // POST /api/matches/{id}/reassign
    [HttpPost("{id:int}/reassign")]
    public async Task<ActionResult<MatchDetailDto>> Reassign(int id, CancellationToken ct)
    {
        var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (match is null)
            return NotFound();

        if (match.IsFinalized || match.IsCompleted)
            return Problem(
                title: "Lineup is locked",
                detail: $"This match is {match.Status.ToString().ToLowerInvariant()}; pairings can't be reassigned.",
                statusCode: StatusCodes.Status409Conflict);

        try
        {
            await lineup.ReassignAsync(id, ct);
        }
        catch (PairingException ex)
        {
            // Rails: redirect with alert: e.message
            return Problem(
                title: "Could not assign pairings",
                detail: ex.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return (await db.GetMatchDetailAsync(id, ct))!;
    }
}
