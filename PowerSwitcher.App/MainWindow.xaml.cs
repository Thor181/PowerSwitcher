using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    private readonly BatteryMonitor _batteryMonitor = new();
    private readonly DispatcherTimer _batteryTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private static readonly int[] TimeoutPresets = { 60, 120, 180, 300, 600, 900, 1800, 3600, 7200, 0 };
    private static readonly byte[] MaxProcessorPresets = { 100, 99, 90, 80, 70, 60, 50 };
    private static readonly (byte Value, string Label)[] EppPresets =
    {
        (0, "Максимальная производительность"),
        (33, "Производительность"),
        (50, "Баланс"),
        (70, "Экономия"),
        (100, "Максимальная экономия"),
    };
    private static readonly int[] LowBatteryPresets = { 10, 15, 20, 25, 30, 40 };

    private ObservableCollection<PowerProfile> _profiles = new();
    private AutoSwitchSettings _autoSwitchSettings;
    private bool _settingsDirty;
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
        _autoSwitchSettings = _profileStore.LoadAutoSwitchSettings();

        LoadSystemData();
        LoadProfiles();
        BindProfileOptions();
        BindSettingsOptions();

        _batteryTimer.Tick += (_, _) => UpdateBattery();

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

    /// <summary>Вызывается после изменения настроек автопереключения.</summary>
    public event Action? AutoSwitchSettingsChanged;

    public AutoSwitchSettings AutoSwitchSettings => _autoSwitchSettings;

    /// <summary>Применяет профиль по имени (для автопереключения). False — профиля нет.</summary>
    public bool ApplyProfileByName(string name)
    {
        var profile = _profiles.FirstOrDefault(p => p.Name == name);
        if (profile is null)
            return false;

        ApplyProfile(profile);
        return true;
    }

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
        UpdateBattery();
        _batteryTimer.Start();

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
        SaveSettingsIfDirty();
        _batteryTimer.Stop();
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
        foreach (var p in new FrameworkElement[] { MainPage, EditPage, AddPage, SettingsPage })
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

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        LoadSettingsIntoControls();
        SetStatus(null);
        ShowPage(SettingsPage);
    }

    private void GoBack()
    {
        SaveIfDirty();
        SaveSettingsIfDirty();
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

        _autoSwitchSettings.ForgetProfile(name);
        _settingsDirty = true;

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

    private void SaveSettingsIfDirty()
    {
        if (!_settingsDirty)
            return;

        _settingsDirty = false;
        if (!_profileStore.SaveAutoSwitchSettings(_autoSwitchSettings))
            SetStatus("Не удалось сохранить настройки автопереключения.");

        AutoSwitchSettingsChanged?.Invoke();
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

        profile.PowerMode = _powerManager.GetPowerMode();

        profile.CpuBoostMode = _powerManager.GetCpuBoostMode();
        profile.MaxProcessorState = _powerManager.GetMaxProcessorState();
        profile.EnergyPerformancePreference = _powerManager.GetEnergyPerformancePreference();

        profile.DisplayOffTimeout = _powerManager.GetDisplayOffTimeout();
        profile.SleepTimeout = _powerManager.GetSleepTimeout();
        profile.AdaptiveBrightness = _powerManager.GetAdaptiveBrightness();

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
            AdaptiveBrightnessCheckBox.IsChecked = profile.AdaptiveBrightness == true;

            SelectOption(PowerModeCombo, profile.PowerMode, v => ((PowerMode)v).GetLabel());
            SelectOption(MaxProcessorCombo, profile.MaxProcessorState, v => FormatMaxProcessor((byte)v));
            SelectOption(EppCombo, profile.EnergyPerformancePreference, v => $"Своё значение ({v})");
            SelectOption(DisplayOffCombo, profile.DisplayOffTimeout, v => ProfileApplier.FormatTimeout((int)v));
            SelectOption(SleepCombo, profile.SleepTimeout, v => ProfileApplier.FormatTimeout((int)v));
        }
        finally
        {
            _loading = false;
        }
    }

    // ---------- Списки с вариантами («Не менять» = null) ----------

    private void BindProfileOptions()
    {
        BindOption(PowerModeCombo,
            Options("Не менять", Enum.GetValues<PowerMode>().Select(m => new OptionItem(m, m.GetLabel()))),
            (p, v) => p.PowerMode = (PowerMode?)v);

        BindOption(MaxProcessorCombo,
            Options("Не менять", MaxProcessorPresets.Select(v => new OptionItem(v, FormatMaxProcessor(v)))),
            (p, v) => p.MaxProcessorState = (byte?)v);

        BindOption(EppCombo,
            Options("Не менять", EppPresets.Select(e => new OptionItem(e.Value, e.Label))),
            (p, v) => p.EnergyPerformancePreference = (byte?)v);

        BindOption(DisplayOffCombo,
            Options("Не менять", TimeoutPresets.Select(t => new OptionItem(t, ProfileApplier.FormatTimeout(t)))),
            (p, v) => p.DisplayOffTimeout = (int?)v);

        BindOption(SleepCombo,
            Options("Не менять", TimeoutPresets.Select(t => new OptionItem(t, ProfileApplier.FormatTimeout(t)))),
            (p, v) => p.SleepTimeout = (int?)v);
    }

    private void BindOption(ComboBox combo, List<OptionItem> items, Action<PowerProfile, object?> apply)
    {
        combo.DisplayMemberPath = nameof(OptionItem.Label);
        combo.ItemsSource = items;
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || _selectedProfile is null || combo.SelectedItem is not OptionItem item)
                return;

            apply(_selectedProfile, item.Value);
            _dirty = true;
        };
    }

    private static List<OptionItem> Options(string nullLabel, IEnumerable<OptionItem> items)
    {
        return items.Prepend(new OptionItem(null, nullLabel)).ToList();
    }

    /// <summary>
    /// Выбирает вариант с данным значением. Если такого пресета нет (значение снято с системы),
    /// добавляет его в список, чтобы не потерять.
    /// </summary>
    private static void SelectOption(ComboBox combo, object? value, Func<object, string> customLabel)
    {
        var items = (List<OptionItem>)combo.ItemsSource;
        var item = items.FirstOrDefault(i => Equals(i.Value, value));

        if (item is null && value is not null)
        {
            item = new OptionItem(value, customLabel(value));
            items.Insert(1, item);
            combo.Items.Refresh();
        }

        combo.SelectedItem = item;
    }

    private static string FormatMaxProcessor(byte percent) => percent switch
    {
        100 => "100% — без ограничений",
        99 => "99% — без турбо",
        _ => $"{percent}%",
    };

    // ---------- Автопереключение ----------

    private void BindSettingsOptions()
    {
        foreach (var combo in new[] { AcProfileCombo, BatteryProfileCombo, LowBatteryProfileCombo })
        {
            combo.DisplayMemberPath = nameof(OptionItem.Label);
            combo.SelectionChanged += (_, _) => OnSettingsChanged();
        }

        LowBatteryThresholdCombo.DisplayMemberPath = nameof(OptionItem.Label);
        LowBatteryThresholdCombo.ItemsSource = LowBatteryPresets.Select(p => new OptionItem(p, $"{p}% и ниже")).ToList();
        LowBatteryThresholdCombo.SelectionChanged += (_, _) => OnSettingsChanged();
    }

    private void LoadSettingsIntoControls()
    {
        _loading = true;
        try
        {
            foreach (var (combo, name) in new[]
            {
                (AcProfileCombo, _autoSwitchSettings.AcProfileName),
                (BatteryProfileCombo, _autoSwitchSettings.BatteryProfileName),
                (LowBatteryProfileCombo, _autoSwitchSettings.LowBatteryProfileName),
            })
            {
                combo.ItemsSource = Options("Не переключать", _profiles.Select(p => new OptionItem(p.Name, p.Name)));
                SelectOption(combo, name, v => (string)v);
            }

            SelectOption(LowBatteryThresholdCombo, _autoSwitchSettings.LowBatteryThreshold, v => $"{v}% и ниже");
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnSettingsChanged()
    {
        if (_loading)
            return;

        _autoSwitchSettings.AcProfileName = (AcProfileCombo.SelectedItem as OptionItem)?.Value as string;
        _autoSwitchSettings.BatteryProfileName = (BatteryProfileCombo.SelectedItem as OptionItem)?.Value as string;
        _autoSwitchSettings.LowBatteryProfileName = (LowBatteryProfileCombo.SelectedItem as OptionItem)?.Value as string;
        if ((LowBatteryThresholdCombo.SelectedItem as OptionItem)?.Value is int threshold)
            _autoSwitchSettings.LowBatteryThreshold = threshold;

        _settingsDirty = true;
    }

    // ---------- Батарея в нижней панели ----------

    private void UpdateBattery()
    {
        var info = _batteryMonitor.GetStatus();

        if (!info.HasBattery || info.Percent is not int percent)
        {
            BatteryIcon.Text = string.Empty;
            BatteryLabel.Text = "Питание от сети";
            PowerLabel.Text = string.Empty;
            return;
        }

        BatteryIcon.Text = GetBatteryGlyph(percent, info.IsOnAc);

        var details = info.IsCharging ? "заряжается"
            : info.IsOnAc ? "от сети"
            : info.TimeRemaining is TimeSpan left ? FormatTimeLeft(left)
            : null;
        BatteryLabel.Text = details is null ? $"{percent}%" : $"{percent}% · {details}";

        if (info.PowerWatts is double watts && Math.Abs(watts) >= 0.05)
        {
            PowerLabel.Text = watts > 0 ? $"+{watts:0.0} Вт" : $"−{-watts:0.0} Вт";
            PowerLabel.ToolTip = watts > 0 ? "Мощность зарядки" : "Текущий расход";
        }
        else
        {
            PowerLabel.Text = string.Empty;
        }
    }

    /// <summary>Глифы Segoe Fluent Icons: Battery0–10 и BatteryCharging0–10.</summary>
    private static string GetBatteryGlyph(int percent, bool charging)
    {
        int level = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);

        if (charging)
        {
            return level switch
            {
                10 => "",
                9 => "",
                _ => ((char)(0xE85A + level)).ToString(),
            };
        }

        return level == 10 ? "" : ((char)(0xE850 + level)).ToString();
    }

    private static string FormatTimeLeft(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours} ч {time.Minutes} мин"
            : $"{time.Minutes} мин";
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

    private void AdaptiveBrightnessCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _selectedProfile is null)
            return;

        _selectedProfile.AdaptiveBrightness = AdaptiveBrightnessCheckBox.IsChecked;
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

    private sealed record OptionItem(object? Value, string Label);
}
