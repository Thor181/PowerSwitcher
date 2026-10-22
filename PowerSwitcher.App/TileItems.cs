using PowerSwitcher.Core;

namespace PowerSwitcher.App;

/// <summary>Плитка профиля на главной странице флайаута.</summary>
public sealed record ProfileTile(PowerProfile Profile, bool IsCurrent)
{
    public string Name => Profile.Name;
}

/// <summary>Плитка «Добавить» в конце списка профилей.</summary>
public sealed record AddTile;
