using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace PowerSwitcher.App;

/// <summary>
/// Значок в области уведомлений: клик левой кнопкой открывает флайаут, правой — меню.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const string Glyph = ""; // молния
    private const int IconSize = 32;

    private readonly Forms.NotifyIcon _notifyIcon;
    private IntPtr _iconHandle;

    public event EventHandler? Clicked;
    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "PowerSwitcher",
            ContextMenuStrip = menu,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                Clicked?.Invoke(this, EventArgs.Empty);
        };

        UpdateIcon();
        ThemeManager.ThemeChanged += OnThemeChanged;
        _notifyIcon.Visible = true;
    }

    public void SetToolTip(string text)
    {
        // Ограничение NotifyIcon.Text — 127 символов
        _notifyIcon.Text = text.Length > 127 ? text[..126] + "…" : text;
    }

    public void ShowNotification(string title, string text)
    {
        _notifyIcon.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        if (_iconHandle != IntPtr.Zero)
            DestroyIcon(_iconHandle);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateIcon();

    private void UpdateIcon()
    {
        using var bitmap = new Bitmap(IconSize, IconSize);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var family = GetIconFontFamily())
        using (var path = new GraphicsPath())
        using (var brush = new SolidBrush(ThemeManager.IsDark ? Color.White : Color.Black))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            path.AddString(Glyph, family, (int)FontStyle.Regular, IconSize, PointF.Empty, StringFormat.GenericTypographic);

            // Вписываем глиф по центру значка с небольшим отступом
            var bounds = path.GetBounds();
            var scale = (IconSize - 4) / Math.Max(bounds.Width, bounds.Height);
            using var matrix = new Matrix();
            matrix.Translate(IconSize / 2f, IconSize / 2f);
            matrix.Scale(scale, scale);
            matrix.Translate(-(bounds.X + bounds.Width / 2), -(bounds.Y + bounds.Height / 2));
            path.Transform(matrix);

            graphics.FillPath(brush, path);
        }

        var handle = bitmap.GetHicon();
        var oldIcon = _notifyIcon.Icon;
        var oldHandle = _iconHandle;

        _notifyIcon.Icon = Icon.FromHandle(handle);
        _iconHandle = handle;

        oldIcon?.Dispose();
        if (oldHandle != IntPtr.Zero)
            DestroyIcon(oldHandle);
    }

    private static FontFamily GetIconFontFamily()
    {
        using var installed = new InstalledFontCollection();
        var name = installed.Families.Any(f => f.Name == "Segoe Fluent Icons")
            ? "Segoe Fluent Icons"
            : "Segoe MDL2 Assets";
        return new FontFamily(name);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
