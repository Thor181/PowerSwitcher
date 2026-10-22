using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PowerSwitcher.App;

/// <summary>
/// Эффекты DWM: фон Mica, скруглённые углы и тёмная рамка окна (Windows 11).
/// </summary>
internal static class WindowEffects
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2;
    // Mica. Акрил (DWMSBT_TRANSIENTWINDOW) у WPF-окна рисуется плоским серым,
    // а Mica выглядит как системная панель и сама переходит на сплошной цвет без прозрачности.
    private const int DWMSBT_MAINWINDOW = 2;

    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x00080000;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    /// <summary>Системный фон окна (DWMWA_SYSTEMBACKDROP_TYPE) появился в Windows 11 22H2.</summary>
    public static bool IsBackdropSupported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// Включает Mica и скругление. Возвращает false, если система не поддерживает Mica —
    /// тогда окну нужен непрозрачный фон.
    /// </summary>
    public static bool Apply(Window window, bool isDark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;

        // Без системного меню DWM не рисует кнопку «Закрыть» сквозь прозрачное окно
        SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);

        SetAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        SetDarkMode(window, isDark);

        if (!IsBackdropSupported)
            return false;

        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = Colors.Transparent;

        return SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_MAINWINDOW);
    }

    public static void SetDarkMode(Window window, bool isDark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, isDark ? 1 : 0);
    }

    private static bool SetAttribute(IntPtr hwnd, int attribute, int value)
    {
        return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;
    }
}
