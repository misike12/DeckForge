using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DeckForge.Validators;

namespace DeckForge.App.Converters;

/// <summary>bool -> Visibility (true = visible).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool -> Visibility (true = collapsed).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool -> green/red status dot.</summary>
public sealed class BoolToStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var brush = new SolidColorBrush(value is true
            ? Color.FromRgb(0x3F, 0xB8, 0x63)
            : Color.FromRgb(0xE5, 0x48, 0x4D));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool -> inverted Visibility (true = collapsed); same as Inverse, aliased for readability.</summary>
public sealed class BoolToInverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool -> inverted bool, two-way (for radio-pair bindings like widget/config region).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>Resolves a DynamicResource-style brush key string (e.g. "Liquid.AccentBrush") to the live brush.</summary>
public sealed class ResourceKeyToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key && System.Windows.Application.Current?.TryFindResource(key) is Brush brush
            ? brush
            : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>ValidationSeverity -> soft card background.</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value switch
        {
            ValidationSeverity.Error => Color.FromArgb(0x2A, 0xE5, 0x48, 0x4D),
            ValidationSeverity.Warning => Color.FromArgb(0x2A, 0xE2, 0x9E, 0x2D),
            _ => Color.FromArgb(0x2A, 0x8A, 0x91, 0x9E),
        };
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
