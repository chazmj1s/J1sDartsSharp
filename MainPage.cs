using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Controls;

namespace J1sDartSharp;

public class MainPage : ContentPage
{
    public MainPage()
    {
        var blazorWebView = new BlazorWebView
        {
            HostPage = "wwwroot/index.html"
        };

        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector      = "#app",
            ComponentType = typeof(Routes)
        });

        Content = blazorWebView;
    }
}
