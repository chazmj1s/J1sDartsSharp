using System.Net;

namespace J1sDartSharp.Services;

/// <summary>
/// A failed league Api call, carrying the server's ProblemDetails when there
/// was one. <see cref="StatusCode"/> is null when the server couldn't be
/// reached at all (offline, wrong address, timeout).
/// </summary>
public sealed class LeagueApiException : Exception
{
    public LeagueApiException(
        HttpStatusCode? statusCode,
        string userMessage,
        IReadOnlyDictionary<string, string[]>? validationErrors = null,
        Exception? inner = null)
        : base(userMessage, inner)
    {
        StatusCode = statusCode;
        UserMessage = userMessage;
        ValidationErrors = validationErrors ?? new Dictionary<string, string[]>();
    }

    /// <summary>HTTP status, or null if the request never got a response.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Short message suitable for an alert/toast.</summary>
    public string UserMessage { get; }

    /// <summary>Field → messages from a 400 validation response (e.g. "Name").</summary>
    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; }

    public bool IsUnreachable => StatusCode is null;
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

    /// <summary>409 — the match lineup is locked (finalized/completed).</summary>
    public bool IsLocked => StatusCode == HttpStatusCode.Conflict;
}
