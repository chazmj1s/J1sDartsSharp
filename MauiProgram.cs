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

        // Register the SQLite database as a singleton
        builder.Services.AddSingleton<DartsDatabase>();

        // SessionHistoryService wraps the DB and provides in-memory cache
        builder.Services.AddSingleton<SessionHistoryService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
