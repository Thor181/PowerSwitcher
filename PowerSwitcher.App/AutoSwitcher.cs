using System.Windows;
using Microsoft.Win32;
using PowerSwitcher.Core;

namespace PowerSwitcher.App;

/// <summary>
/// Применяет профиль автоматически при смене источника питания и низком заряде.
/// Срабатывает только когда меняется нужный профиль, поэтому не перебивает ручной выбор
/// на каждом изменении процента заряда.
/// </summary>
internal sealed class AutoSwitcher : IDisposable
{
    private readonly MainWindow _flyout;
    private readonly TrayIcon _trayIcon;
    private readonly BatteryMonitor _batteryMonitor = new();
    private string? _lastTarget;

    public AutoSwitcher(MainWindow flyout, TrayIcon trayIcon)
    {
        _flyout = flyout;
        _trayIcon = trayIcon;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _flyout.AutoSwitchSettingsChanged += OnSettingsChanged;
    }

    public void Evaluate()
    {
        var settings = _flyout.AutoSwitchSettings;
        var battery = _batteryMonitor.GetStatus();
        var target = AutoSwitchPolicy.SelectProfile(settings, battery.IsOnAc, battery.Percent);

        if (target == _lastTarget)
            return;

        _lastTarget = target;
        if (target is null || target == _flyout.CurrentProfileName)
            return;

        if (!_flyout.ApplyProfileByName(target))
            return;

        var reason = battery.IsOnAc ? "питание от сети"
            : target == settings.LowBatteryProfileName && target != settings.BatteryProfileName
                ? $"заряд {battery.Percent}%"
                : "питание от батареи";
        _trayIcon.ShowNotification($"Профиль «{target}»", $"Применён автоматически: {reason}.");
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _flyout.AutoSwitchSettingsChanged -= OnSettingsChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // StatusChange — подключение/отключение зарядки и изменение заряда
        if (e.Mode == PowerModes.StatusChange)
            Application.Current?.Dispatcher.BeginInvoke(Evaluate);
    }

    private void OnSettingsChanged()
    {
        // Новые настройки — оцениваем заново, даже если состояние питания не менялось
        _lastTarget = null;
        Evaluate();
    }
}
