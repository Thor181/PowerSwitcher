using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PowerSwitcher.App;

/// <summary>
/// Следит за темой и акцентным цветом Windows и подставляет нужные ресурсы в приложение.
/// </summary>
internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    // Палитра акцента Windows по умолчанию: Light3, Light2, Light1, Base, Dark1, Dark2, Dark3
    private static readonly Color[] DefaultPalette =
    {
        Color.FromRgb(0x99, 0xEB, 0xFF),
        Color.FromRgb(0x4C, 0xC2, 0xFF),
        Color.FromRgb(0x00, 0x91, 0xF8),
        Color.FromRgb(0x00, 0x78, 0xD4),
        Color.FromRgb(0x00, 0x67, 0xC0),
        Color.FromRgb(0x00, 0x3E, 0x92),
        Color.FromRgb(0x00, 0x1A, 0x68),
    };

    private static Color _accent;
    private static bool _initialized;

    /// <summary>Тёмная ли тема у оболочки Windows (панель задач, флайауты).</summary>
    public static bool IsDark { get; private set; }

    public static event EventHandler? ThemeChanged;

    public static void Initialize()
    {
        Apply();
        SystemEvents.UserPreferenceChanged += (_, _) =>
            Application.Current?.Dispatcher.BeginInvoke(Apply);
    }

    private static void Apply()
    {
        var isDark = ReadIsDark();
        var palette = ReadAccentPalette();
        // Как в WinUI: в светлой теме заливка акцентом — Dark1, в тёмной — Light2
        var accent = isDark ? palette[1] : palette[4];

        if (_initialized && isDark == IsDark && accent == _accent)
            return;

        _initialized = true;
        IsDark = isDark;
        _accent = accent;

        var resources = Application.Current.Resources;
        resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(isDark ? "Dark" : "Light")}.xaml"),
        };

        resources["AccentFill"] = Freeze(new SolidColorBrush(accent));
        resources["AccentFillHover"] = Freeze(new SolidColorBrush(accent) { Opacity = 0.9 });
        resources["AccentFillPressed"] = Freeze(new SolidColorBrush(accent) { Opacity = 0.8 });
        resources["TextOnAccent"] = isDark ? Brushes.Black : Brushes.White;

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool ReadIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int light && light == 0;
    }

    private static Color[] ReadAccentPalette()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AccentKey);
        if (key?.GetValue("AccentPalette") is not byte[] data || data.Length < DefaultPalette.Length * 4)
            return DefaultPalette;

        var palette = new Color[DefaultPalette.Length];
        for (int i = 0; i < palette.Length; i++)
            palette[i] = Color.FromRgb(data[i * 4], data[i * 4 + 1], data[i * 4 + 2]);

        return palette;
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
