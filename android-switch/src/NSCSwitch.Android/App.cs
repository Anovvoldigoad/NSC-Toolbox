using Microsoft.Maui.Controls;

namespace NSCSwitch.AndroidApp;

public sealed class App : Application
{
    public App() => UserAppTheme = AppTheme.Dark;

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new MainPage());
}
