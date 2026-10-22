namespace PowerSwitcher.Core;

public static class CpuBoostModeExtensions
{
    public static string GetLabel(this CpuBoostMode mode) => mode switch
    {
        CpuBoostMode.Disabled => "Отключён",
        CpuBoostMode.Enabled => "Включён",
        CpuBoostMode.Aggressive => "Агрессивный",
        CpuBoostMode.EfficientEnabled => "Эффективный",
        CpuBoostMode.EfficientAggressive => "Эффективный агрессивный",
        CpuBoostMode.AggressiveAtGuaranteed => "Агрессивный (при гарантированной)",
        CpuBoostMode.EfficientAggressiveAtGuaranteed => "Эффективный агрессивный (при гарантированной)",
        _ => mode.ToString(),
    };
}
