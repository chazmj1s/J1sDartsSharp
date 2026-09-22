using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace J1sDartSharp;

public class App : Application
{
    public App()
    {
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage());
    }
}
