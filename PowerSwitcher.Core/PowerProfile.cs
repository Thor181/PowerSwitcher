namespace PowerSwitcher.Core;

public sealed class PowerProfile
{
    public string Name { get; set; } = string.Empty;

    public string? DisplayDeviceName { get; set; }

    public int? RefreshRate { get; set; }

    public Guid? PowerSchemeId { get; set; }

    public PowerMode? PowerMode { get; set; }

    public CpuBoostMode? CpuBoostMode { get; set; }

    /// <summary>Максимальное состояние процессора, % (99 и ниже отключают турбо).</summary>
    public byte? MaxProcessorState { get; set; }

    /// <summary>Приоритет энергоэффективности (EPP): 0 — производительность, 100 — экономия.</summary>
    public byte? EnergyPerformancePreference { get; set; }

    /// <summary>Через сколько секунд отключать экран; 0 — никогда.</summary>
    public int? DisplayOffTimeout { get; set; }

    /// <summary>Через сколько секунд уходить в сон; 0 — никогда.</summary>
    public int? SleepTimeout { get; set; }

    public bool? AdaptiveBrightness { get; set; }

    public byte? Brightness { get; set; }

    public bool? EnergySaver { get; set; }

    public override string ToString() => Name;
}
