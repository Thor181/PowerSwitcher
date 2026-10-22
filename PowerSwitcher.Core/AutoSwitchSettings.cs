namespace PowerSwitcher.Core;

/// <summary>Какой профиль применять автоматически в зависимости от питания.</summary>
public sealed class AutoSwitchSettings
{
    public string? AcProfileName { get; set; }

    public string? BatteryProfileName { get; set; }

    public string? LowBatteryProfileName { get; set; }

    public int LowBatteryThreshold { get; set; } = 20;

    /// <summary>Убирает ссылки на удалённый профиль.</summary>
    public void ForgetProfile(string name)
    {
        if (AcProfileName == name)
            AcProfileName = null;
        if (BatteryProfileName == name)
            BatteryProfileName = null;
        if (LowBatteryProfileName == name)
            LowBatteryProfileName = null;
    }
}

public static class AutoSwitchPolicy
{
    /// <summary>
    /// Профиль, который должен действовать при данном состоянии питания,
    /// либо <c>null</c>, если для этого состояния ничего не задано.
    /// </summary>
    public static string? SelectProfile(AutoSwitchSettings settings, bool isOnAc, int? batteryPercent)
    {
        if (isOnAc)
            return settings.AcProfileName;

        if (settings.LowBatteryProfileName != null
            && batteryPercent is int percent
            && percent <= settings.LowBatteryThreshold)
        {
            return settings.LowBatteryProfileName;
        }

        return settings.BatteryProfileName;
    }
}
