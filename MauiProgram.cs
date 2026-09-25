using Microsoft.Extensions.Logging;
using J1sDartSharp.Services;

namespace J1sDartSharp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

        // ── Practice sessions (local-only, per device) ──────────────────────

        // Register the SQLite database as a singleton
        builder.Services.AddSingleton<DartsDatabase>();

        // SessionHistoryService wraps the DB and provides in-memory cache
        builder.Services.AddSingleton<SessionHistoryService>();

        // ── League / match night (shared, via J1sDartSharp.Api) ─────────────

        // Where the league Api lives. Keep the trailing slash — the client's
        // request paths are relative to it.
        // localhost only works for the Windows app on the same PC as the Api;
        // point this at the Ubuntu server before deploying.
        builder.Services.AddSingleton(new LeagueApiOptions
        {
            BaseAddress = new Uri("http://localhost:5180/")
        });

        // One HttpClient for the app's lifetime.
        builder.Services.AddSingleton<ILeagueApiClient, LeagueApiClient>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif

        return builder.Build();
    }
}
