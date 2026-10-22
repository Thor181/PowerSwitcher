using System.Windows;

namespace PowerSwitcher.App;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private MainWindow? _flyout;
    private AutoSwitcher? _autoSwitcher;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ThemeManager.Initialize();

        var flyout = new MainWindow();
        var trayIcon = new TrayIcon();
        _flyout = flyout;
        _trayIcon = trayIcon;

        trayIcon.Clicked += (_, _) => flyout.ToggleFlyout();
        trayIcon.OpenRequested += (_, _) => flyout.ShowFlyout();
        trayIcon.ExitRequested += (_, _) => Shutdown();

        flyout.CurrentProfileChanged += UpdateToolTip;
        UpdateToolTip(flyout.CurrentProfileName);

        _autoSwitcher = new AutoSwitcher(flyout, trayIcon);
        _autoSwitcher.Evaluate();

        // С ключом --tray приложение стартует свёрнутым в область уведомлений
        if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
            flyout.ShowFlyout();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _flyout?.HideFlyout();
        _autoSwitcher?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void UpdateToolTip(string? profileName)
    {
        _trayIcon?.SetToolTip(profileName is null
            ? "PowerSwitcher"
            : $"PowerSwitcher — {profileName}");
    }
}
