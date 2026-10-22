namespace PowerSwitcher.Core;

/// <summary>Режим питания Windows 11 («Параметры → Питание → Режим питания»).</summary>
public enum PowerMode
{
    BestEfficiency,
    Balanced,
    BestPerformance,
}

public static class PowerModeExtensions
{
    public static string GetLabel(this PowerMode mode) => mode switch
    {
        PowerMode.BestEfficiency => "Лучшая энергоэффективность",
        PowerMode.Balanced => "Сбалансированный",
        PowerMode.BestPerformance => "Максимальная производительность",
        _ => mode.ToString(),
    };
}
