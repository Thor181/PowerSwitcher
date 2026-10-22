using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PowerSwitcher.Core;

namespace PowerSwitcher.App;

/// <summary>
/// Флайаут в стиле быстрых настроек Windows 11: появляется над областью уведомлений
/// и скрывается при потере фокуса.
/// </summary>
public partial class MainWindow : Window
{
    private const double ScreenMargin = 12;

    private readonly DisplayManager _displayManager = new();
    private readonly PowerManager _powerManager = new();
    private readonly BrightnessManager _brightnessManager = new();
    private readonly ProfileStore _profileStore = new();
    private readonly ProfileApplier _profileApplier;

    private ObservableCollection<PowerProfile> _profiles = new();
    private PowerProfile? _selectedProfile;
    private IReadOnlyList<DisplayInfo> _displays = Array.Empty<DisplayInfo>();
    private IReadOnlyList<PowerSchemeInfo> _schemes = Array.Empty<PowerSchemeInfo>();
    private IReadOnlyList<BoostModeItem> _boostModes = Array.Empty<BoostModeItem>();
    private bool _loading;
    private bool _dirty;
    private string? _currentProfileName;
    private DateTime _hiddenAt;
    private FrameworkElement _currentPage;

    public MainWindow()
    {
        InitializeComponent();
        _profileApplier = new ProfileApplier(_displayManager, _powerManager, _brightnessManager);
        _currentPage = MainPage;

        LoadSystemData();
        LoadProfiles();

        SizeChanged += (_, _) => PositionWindow();
        Deactivated += (_, _) => HideFlyout();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4 только прячет флайаут; при завершении приложения отмена игнорируется
        e.Cancel = true;
        HideFlyout();
        base.OnClosing(e);
    }

    /// <summary>Вызывается при смене текущего (применённого) профиля.</summary>
    public event Action<string?>? CurrentProfileChanged;

    public string? CurrentProfileName => _currentProfileName;

    // ---------- Показ и скрытие флайаута ----------

    public void ToggleFlyout()
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }

        // Клик по значку в трее сначала отнимает у окна фокус (и скрывает его),
        // а уже потом приходит сам клик — не открываем окно повторно.
        if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 400)
            return;

        ShowFlyout();
    }

    public void ShowFlyout()
    {
        if (IsVisible)
        {
            Activate();
            return;
        }

        ShowPage(MainPage, animate: false);
        SetStatus(null);

        MaxHeight = SystemParameters.WorkArea.Height - ScreenMargin * 2;
        RootBorder.Measure(new Size(Width, MaxHeight));
        PositionWindow(RootBorder.DesiredSize.Height);

        Show();
        Activate();
        PlayShowAnimation();
    }

    public void HideFlyout()
    {
        if (!IsVisible)
            return;

        SaveIfDirty();
        Hide();
        _hiddenAt = DateTime.UtcNow;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (WindowEffects.Apply(this, ThemeManager.IsDark))
            RootBorder.Background = Brushes.Transparent;

        ThemeManager.ThemeChanged += (_, _) => WindowEffects.SetDarkMode(this, ThemeManager.IsDark);
    }

    private void PositionWindow() => PositionWindow(ActualHeight);

    private void PositionWindow(double height)
    {
        var workArea = SystemParameters.WorkArea;

        // По умолчанию — правый нижний угол, над панелью задач
        Left = workArea.Right - Width - ScreenMargin;
        Top = workArea.Bottom - height - ScreenMargin;

        // Панель задач слева или сверху (Windows 10)
        if (workArea.Left > 0)
            Left = workArea.Left + ScreenMargin;
        if (workArea.Top > 0)
            Top = workArea.Top + ScreenMargin;
    }

    private void PlayShowAnimation()
    {
        var fromTop = SystemParameters.WorkArea.Top > 0;
        var slide = new DoubleAnimation(fromTop ? -40 : 40, 0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));

        SlideTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        RootGrid.BeginAnimation(OpacityProperty, fade);
    }

    private void ShowPage(FrameworkElement page, bool animate = true)
    {
        foreach (var p in new FrameworkElement[] { MainPage, EditPage, AddPage })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;

        var forward = page != MainPage;
        _currentPage = page;

        if (!animate || page.RenderTransform is not TranslateTransform transform)
            return;

        var slide = new DoubleAnimation(forward ? 24 : -24, 0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        transform.BeginAnimation(TranslateTransform.XProperty, slide);
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        if (_currentPage == MainPage)
            HideFlyout();
        else
            GoBack();

        e.Handled = true;
    }

    // ---------- Данные системы (варианты для списков) ----------

    private void LoadSystemData()
    {
        _displays = _displayManager.GetDisplays();
        DisplayCombo.ItemsSource = _displays;

        _schemes = _powerManager.GetPowerSchemes();
        SchemeCombo.ItemsSource = _schemes;

        _boostModes = Enum.GetValues<CpuBoostMode>()
            .Select(m => new BoostModeItem(m, m.GetLabel()))
            .ToArray();
        BoostCombo.ItemsSource = _boostModes;
    }

    // ---------- Профили ----------

    private void LoadProfiles()
    {
        _profiles = new ObservableCollection<PowerProfile>(_profileStore.Load());

        var storedName = _profileStore.LoadCurrentProfileName();
        _currentProfileName = _profiles.Any(p => p.Name == storedName) ? storedName : null;

        RefreshTiles();
    }

    private void RefreshTiles()
    {
        var tiles = _profiles
            .Select(p => (object)new ProfileTile(p, p.Name == _currentProfileName))
            .Append(new AddTile())
            .ToList();
        TilesList.ItemsSource = tiles;

        CurrentProfileLabel.Text = _currentProfileName ?? "Профиль не выбран";
        CurrentProfileChanged?.Invoke(_currentProfileName);
    }

    private void ProfileTile_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ProfileTile tile)
            ApplyProfile(tile.Profile);
    }

    private void ProfileTileEdit_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ProfileTile tile)
            OpenEditor(tile.Profile);
    }

    private void AddTile_Click(object sender, RoutedEventArgs e)
    {
        SetStatus(null);
        ProfileNameBox.Clear();
        ShowPage(AddPage);
        ProfileNameBox.Focus();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    private void GoBack()
    {
        SaveIfDirty();
        _selectedProfile = null;
        SetStatus(null);
        RefreshTiles();
        ShowPage(MainPage);
    }

    private void OpenEditor(PowerProfile profile)
    {
        _selectedProfile = profile;
        EditTitle.Text = profile.Name;
        LoadProfileIntoControls(profile);
        SetStatus(null);
        ShowPage(EditPage);
    }

    private void ProfileNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddProfile_Click(sender, e);
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (name.Length == 0)
            name = $"Профиль {_profiles.Count + 1}";

        if (_profiles.Any(p => p.Name == name))
        {
            SetStatus($"Профиль «{name}» уже существует.");
            return;
        }

        var profile = SnapshotSystemSettings(name);
        _profiles.Add(profile);

        if (!Persist())
        {
            SetStatus("Не удалось сохранить профили.");
            return;
        }

        RefreshTiles();
        OpenEditor(profile);
        SetStatus($"Профиль «{name}» добавлен и сохранён.");
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null)
            return;

        var name = _selectedProfile.Name;
        _profiles.Remove(_selectedProfile);
        _selectedProfile = null;
        _dirty = false;

        if (!Persist())
        {
            SetStatus("Не удалось сохранить профили.");
            return;
        }

        if (_currentProfileName == name)
        {
            _currentProfileName = null;
            _profileStore.SaveCurrentProfileName(null);
        }

        GoBack();
        SetStatus($"Профиль «{name}» удалён.");
    }

    private void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null)
            return;

        SaveIfDirty();
        ApplyProfile(_selectedProfile);
    }

    private void ApplyProfile(PowerProfile profile)
    {
        var result = _profileApplier.Apply(profile);

        _currentProfileName = profile.Name;
        _profileStore.SaveCurrentProfileName(_currentProfileName);
        RefreshTiles();

        SetStatus(FormatResult(result));
    }

    private void SaveIfDirty()
    {
        if (!_dirty)
            return;

        _dirty = false;
        if (!Persist())
            SetStatus("Не удалось сохранить профили.");
    }

    private bool Persist()
    {
        return _profileStore.Save(_profiles);
    }

    private PowerProfile SnapshotSystemSettings(string name)
    {
        var profile = new PowerProfile { Name = name };

        if (_displays.Count > 0)
        {
            profile.DisplayDeviceName = _displays[0].DeviceName;
            profile.RefreshRate = _displays[0].RefreshRate;
        }

        var scheme = _powerManager.GetActiveScheme();
        if (scheme != null)
            profile.PowerSchemeId = scheme.Id;

        profile.CpuBoostMode = _powerManager.GetCpuBoostMode();

        profile.Brightness = _brightnessManager.GetBrightness();

        profile.EnergySaver = _powerManager.GetEnergySaver();

        return profile;
    }

    private void LoadProfileIntoControls(PowerProfile profile)
    {
        _loading = true;
        try
        {
            var display = _displays.FirstOrDefault(d => d.DeviceName == profile.DisplayDeviceName);
            DisplayCombo.SelectedItem = display;

            RefreshRateCombo.ItemsSource = display != null
                ? _displayManager.GetSupportedRefreshRates(display.DeviceName)
                : Array.Empty<int>();
            RefreshRateCombo.SelectedItem = profile.RefreshRate;

            SchemeCombo.SelectedItem = _schemes.FirstOrDefault(s => s.Id == profile.PowerSchemeId);
            BoostCombo.SelectedItem = _boostModes.FirstOrDefault(b => b.Mode == profile.CpuBoostMode);

            if (profile.Brightness is byte brightness)
            {
                BrightnessSlider.Value = brightness;
                BrightnessValueLabel.Text = $"{brightness}%";
            }
            else
            {
                BrightnessSlider.Value = 0;
                BrightnessValueLabel.Text = "—";
            }

            EnergySaverCheckBox.IsChecked = profile.EnergySaver == true;
        }
        finally
        {
            _loading = false;
        }
    }

    private static string FormatResult(ProfileApplyResult result)
    {
        if (result.Actions.Count == 0)
            return $"Профиль «{result.ProfileName}» не содержит настроек.";

        var lines = result.Actions.Select(a =>
            $"{(a.Success ? "✓" : "✗")} {a.Setting}: {a.Detail}");

        return $"Профиль «{result.ProfileName}» применён:\n" + string.Join("\n", lines);
    }

    // ---------- Редактирование настроек профиля ----------

    private void DisplayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _selectedProfile is null)
            return;

        if (DisplayCombo.SelectedItem is DisplayInfo display)
        {
            RefreshRateCombo.ItemsSource = _displayManager.GetSupportedRefreshRates(display.DeviceName);
            RefreshRateCombo.SelectedItem = display.RefreshRate;
            _selectedProfile.DisplayDeviceName = display.DeviceName;
        }
        else
        {
            RefreshRateCombo.ItemsSource = null;
            RefreshRateCombo.SelectedItem = null;
            _selectedProfile.DisplayDeviceName = null;
            _selectedProfile.RefreshRate = null;
        }

        _dirty = true;
    }

    private void RefreshRateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _selectedProfile is null)
            return;

        _selectedProfile.RefreshRate = RefreshRateCombo.SelectedItem as int?;
        _dirty = true;
    }

    private void SchemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _selectedProfile is null)
            return;

        _selectedProfile.PowerSchemeId = (SchemeCombo.SelectedItem as PowerSchemeInfo)?.Id;
        _dirty = true;
    }

    private void BoostCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _selectedProfile is null)
            return;

        _selectedProfile.CpuBoostMode = (BoostCombo.SelectedItem as BoostModeItem)?.Mode;
        _dirty = true;
    }

    private void EnergySaverCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _selectedProfile is null)
            return;

        _selectedProfile.EnergySaver = EnergySaverCheckBox.IsChecked;
        _dirty = true;
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
            return;

        BrightnessValueLabel.Text = $"{(int)BrightnessSlider.Value}%";

        if (_selectedProfile is not null)
        {
            _selectedProfile.Brightness = (byte)Math.Round(BrightnessSlider.Value);
            _dirty = true;
        }
    }

    private void SetStatus(string? message)
    {
        StatusText.Text = message;
        StatusText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed record BoostModeItem(CpuBoostMode Mode, string Label);
}
