namespace PowerSwitcher.Core;

public sealed class PowerProfile
{
    public string Name { get; set; } = string.Empty;

    public string? DisplayDeviceName { get; set; }

    public int? RefreshRate { get; set; }

    public Guid? PowerSchemeId { get; set; }

    public CpuBoostMode? CpuBoostMode { get; set; }

    public byte? Brightness { get; set; }

    public bool? EnergySaver { get; set; }

    public override string ToString() => Name;
}
