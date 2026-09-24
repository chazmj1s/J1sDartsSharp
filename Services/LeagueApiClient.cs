using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using J1sDartSharp.Shared.Contracts;

namespace J1sDartSharp.Services;

/// <summary>Where the league Api lives. Set in MauiProgram.</summary>
public sealed class LeagueApiOptions
{
    public required Uri BaseAddress { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Thin HttpClient wrapper over the J1sDartSharp.Api endpoints.
/// Registered as a singleton; one HttpClient for the app's lifetime.
/// JSON shapes and enum/date handling come from the Shared contracts, so the
/// only serializer setting here is the standard web (camelCase) defaults.
/// </summary>
public sealed class LeagueApiClient : ILeagueApiClient, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public LeagueApiClient(LeagueApiOptions options)
    {
        _http = new HttpClient
        {
            BaseAddress = options.BaseAddress,
            Timeout = options.Timeout
        };
    }

    public void Dispose() => _http.Dispose();

    // ── Players ─────────────────────────────────────────────────────────

    public Task<RosterDto> GetRosterAsync(CancellationToken ct = default) =>
        SendAsync<RosterDto>(HttpMethod.Get, "api/players", null, ct);

    public Task<PlayerDto> GetPlayerAsync(int playerId, CancellationToken ct = default) =>
        SendAsync<PlayerDto>(HttpMethod.Get, $"api/players/{playerId}", null, ct);

    public Task<PlayerDto> CreatePlayerAsync(SavePlayerRequest request, CancellationToken ct = default) =>
        SendAsync<PlayerDto>(HttpMethod.Post, "api/players", request, ct);

    public Task<PlayerDto> UpdatePlayerAsync(int playerId, SavePlayerRequest request, CancellationToken ct = default) =>
        SendAsync<PlayerDto>(HttpMethod.Put, $"api/players/{playerId}", request, ct);

    public Task RemovePlayerAsync(int playerId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/players/{playerId}", null, ct);

    // ── Matches ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MatchSummaryDto>> GetMatchesAsync(CancellationToken ct = default) =>
        await SendAsync<List<MatchSummaryDto>>(HttpMethod.Get, "api/matches", null, ct);

    public Task<MatchDetailDto> GetMatchAsync(int matchId, CancellationToken ct = default) =>
        SendAsync<MatchDetailDto>(HttpMethod.Get, $"api/matches/{matchId}", null, ct);

    public Task<MatchDetailDto> CreateMatchAsync(SaveMatchRequest request, CancellationToken ct = default) =>
        SendAsync<MatchDetailDto>(HttpMethod.Post, "api/matches", request, ct);

    public Task<MatchDetailDto> UpdateMatchAsync(int matchId, SaveMatchRequest request, CancellationToken ct = default) =>
        SendAsync<MatchDetailDto>(HttpMethod.Put, $"api/matches/{matchId}", request, ct);

    public Task DeleteMatchAsync(int matchId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/matches/{matchId}", null, ct);

    public Task<MatchDetailDto> FinalizeMatchAsync(int matchId, CancellationToken ct = default) =>
        SendAsync<MatchDetailDto>(HttpMethod.Post, $"api/matches/{matchId}/finalize", null, ct);

    public Task<MatchDetailDto> ReassignPairingsAsync(int matchId, CancellationToken ct = default) =>
        SendAsync<MatchDetailDto>(HttpMethod.Post, $"api/matches/{matchId}/reassign", null, ct);

    // ── Games ───────────────────────────────────────────────────────────

    public Task<MatchDetailDto> UpdateGamePlayersAsync(
        int matchId, int gameId, IReadOnlyList<int> playerIds, CancellationToken ct = default) =>
        SendAsync<MatchDetailDto>(HttpMethod.Put, $"api/matches/{matchId}/games/{gameId}",
            new UpdateGamePlayersRequest { PlayerIds = playerIds }, ct);

    public Task<MatchDetailDto> CompleteGameAsync(
        int matchId, int gameId, int homeScore, int awayScore, CancellationToken ct = default) =>
        SendAsync<MatchDetailDto>(HttpMethod.Post, $"api/matches/{matchId}/games/{gameId}/complete",
            new CompleteGameRequest { HomeScore = homeScore, AwayScore = awayScore }, ct);

    // ── Absences ────────────────────────────────────────────────────────

    public Task<MatchDetailDto> MarkAbsentAsync(
        int matchId, int playerId, string? reason = null, CancellationToken ct = default) =>
        SendAsync<MatchDetailDto>(HttpMethod.Post, $"api/matches/{matchId}/absences",
            new CreateAbsenceRequest { PlayerId = playerId, Reason = reason }, ct);

    public Task<MatchDetailDto> MarkPresentAsync(int matchId, int absenceId, CancellationToken ct = default) =>
        SendAsync<MatchDetailDto>(HttpMethod.Delete, $"api/matches/{matchId}/absences/{absenceId}", null, ct);

    // ── Plumbing ────────────────────────────────────────────────────────

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, path, body, ct);

        var result = await response.Content.ReadFromJsonAsync<T>(Json, ct);
        return result ?? throw new LeagueApiException(response.StatusCode, "The server returned an empty response.");
    }

    private async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, path, body, ct);
    }

    /// <summary>
    /// Sends the request and returns a successful response, or throws
    /// LeagueApiException built from the server's ProblemDetails.
    /// </summary>
    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LeagueApiException(null, "Can't reach the league server. Check your connection.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout (not a caller cancellation).
            throw new LeagueApiException(null, "The league server took too long to respond.", inner: ex);
        }

        if (response.IsSuccessStatusCode)
            return response;

        try
        {
            throw await ToExceptionAsync(response, ct);
        }
        finally
        {
            response.Dispose();
        }
    }

    private static async Task<LeagueApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        ProblemPayload? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(Json, ct);
        }
        catch (JsonException)
        {
            // Not a ProblemDetails body — fall back to the status code below.
        }
        catch (NotSupportedException)
        {
            // Non-JSON content type.
        }

        var firstValidationError = problem?.Errors?.Values.SelectMany(v => v).FirstOrDefault();

        var message =
            (response.StatusCode == HttpStatusCode.BadRequest ? firstValidationError : null)
            ?? problem?.Detail
            ?? firstValidationError
            ?? problem?.Title
            ?? $"Request failed ({(int)response.StatusCode} {response.ReasonPhrase}).";

        return new LeagueApiException(response.StatusCode, message, problem?.Errors);
    }

    /// <summary>The parts of RFC 7807 ProblemDetails the app uses.</summary>
    private sealed record ProblemPayload(
        string? Title,
        string? Detail,
        int? Status,
        Dictionary<string, string[]>? Errors);
}
