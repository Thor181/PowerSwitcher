using System.Windows;

namespace PowerSwitcher.App;

/// <summary>
/// Вспомогательные attached-свойства для стилей в духе Windows 11.
/// </summary>
public static class Fluent
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Fluent), new PropertyMetadata(new CornerRadius(4)));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(Fluent), new PropertyMetadata(false));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);
}
