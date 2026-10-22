namespace PowerSwitcher.Core;

public sealed class ProfileAction
{
    public ProfileAction(string setting, string detail, bool success)
    {
        Setting = setting;
        Detail = detail;
        Success = success;
    }

    public string Setting { get; }

    public string Detail { get; }

    public bool Success { get; }
}

public sealed class ProfileApplyResult
{
    public required string ProfileName { get; init; }

    public IReadOnlyList<ProfileAction> Actions { get; init; } = Array.Empty<ProfileAction>();

    public bool AllSucceeded => Actions.Count > 0 && Actions.All(a => a.Success);
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ProfileApplier
{
    private readonly DisplayManager _displayManager;
    private readonly PowerManager _powerManager;
    private readonly BrightnessManager _brightnessManager;

    public ProfileApplier(
        DisplayManager displayManager,
        PowerManager powerManager,
        BrightnessManager brightnessManager)
    {
        _displayManager = displayManager;
        _powerManager = powerManager;
        _brightnessManager = brightnessManager;
    }

    public ProfileApplyResult Apply(PowerProfile profile)
    {
        var actions = new List<ProfileAction>();

        if (profile.RefreshRate is int rate && !string.IsNullOrEmpty(profile.DisplayDeviceName))
        {
            bool ok = _displayManager.SetRefreshRate(profile.DisplayDeviceName, rate);
            actions.Add(new ProfileAction("Частота обновления", $"{rate} Гц", ok));
        }

        if (profile.PowerSchemeId is Guid schemeId)
        {
            bool ok = _powerManager.SetActiveScheme(schemeId);
            actions.Add(new ProfileAction("Схема питания", GetSchemeName(schemeId), ok));
        }

        if (profile.CpuBoostMode is CpuBoostMode mode)
        {
            bool ok = _powerManager.SetCpuBoostMode(mode);
            actions.Add(new ProfileAction("Turbo Boost", mode.GetLabel(), ok));
        }

        if (profile.EnergySaver is bool energySaver)
        {
            bool ok = _powerManager.SetEnergySaver(energySaver);
            actions.Add(new ProfileAction("Экономия заряда", energySaver ? "Вкл" : "Выкл", ok));
        }

        // Яркость — последней: смена схемы и Turbo Boost переактивируют схему питания,
        // и Windows восстанавливает яркость, сохранённую в схеме, затирая только что выставленную.
        if (profile.Brightness is byte brightness)
        {
            bool ok = _brightnessManager.SetBrightness(brightness);
            actions.Add(new ProfileAction("Яркость", $"{brightness}%", ok));
        }

        return new ProfileApplyResult
        {
            ProfileName = profile.Name,
            Actions = actions,
        };
    }

    private string GetSchemeName(Guid schemeId)
    {
        var scheme = _powerManager.GetPowerSchemes().FirstOrDefault(s => s.Id == schemeId);
        return scheme?.Name ?? schemeId.ToString("B");
    }
}
