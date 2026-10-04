using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DeckForge.App.Services;
using DeckForge.Core.Settings;
using DeckForge.CliAdapter;
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

/// <summary>
/// Binds a radio button's <c>IsChecked</c> to an enum member named by <c>ConverterParameter</c>.
/// </summary>
/// <remarks>
/// The theme radios used a Checked handler in code-behind that wrote a named field, so the page
/// owned the state and the view model could not. Three radios, one enum and a parameter is what it
/// takes to bind them properly.
/// </remarks>
public sealed class EnumToAppThemeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = parameter as string;
        return value is AppTheme theme && string.Equals(theme.ToString(), name, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true
            ? Enum.TryParse(parameter as string, ignoreCase: true, out AppTheme theme)
                ? theme
                : DependencyProperty.UnsetValue
            : Binding.DoNothing;
}

/// <summary>
/// Binds a radio button's <c>IsChecked</c> to any enum member named by <c>ConverterParameter</c>.
/// </summary>
/// <remarks>
/// A second converter rather than making <see cref="EnumToAppThemeConverter"/> generic, because a generic
/// one loses the check that a converter parameter is a member of <em>this</em> enum: with a generic
/// converter a typo in <c>ConverterParameter</c> compiles, binds to <see cref="DependencyProperty.UnsetValue"/>
/// and simply renders an unchecked radio, which is a control the user can click and that does nothing.
/// Two small converters and a failure at the point of use beat one flexible one and a silent no-op.
/// </remarks>
public sealed class EnumToMotionPreferenceConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is MotionPreference preference
        && string.Equals(preference.ToString(), parameter as string, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true
            ? Enum.TryParse(parameter as string, ignoreCase: true, out MotionPreference preference)
                ? preference
                : DependencyProperty.UnsetValue
            : Binding.DoNothing;
}

/// <summary>Collapses when the bound int is zero, so an empty list shows no button.</summary>
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// A <see cref="DoctorCheck"/> to a status colour that respects its severity.
/// </summary>
/// <remarks>
/// The flat bool converter painted every failed check red, so an absent Macro Deck desktop app -
/// which the bundled stub host does not need - looked identical to a missing .NET SDK.
/// </remarks>
public sealed class DoctorCheckToStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value is DoctorCheck { Ok: true }
            ? Color.FromRgb(0x3F, 0xB8, 0x63)
            : value is DoctorCheck { Severity: DoctorSeverity.Optional }
                ? Color.FromRgb(0x9A, 0x9A, 0x9A)
                : Color.FromRgb(0xE5, 0x48, 0x4D);

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Collapses the fix hint on a check that passed, so a green row has no trailing text.</summary>
public sealed class DoctorCheckFixHintToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DoctorCheck { Ok: false, FixHint: not null } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Shows a check's install button only when DeckForge can actually act on it.
/// </summary>
/// <remarks>
/// A button that appears and then says it cannot help is worse than no button, so this is decided
/// from the check itself rather than from the page: it has to be failing, and it has to either
/// carry an install command or be the Macro Deck download.
/// </remarks>
public sealed class DoctorCheckInstallableToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DoctorCheck { Ok: false } check
            && (check.InstallCommand is { Length: > 0 } || check.Id == "macrodeck-host")
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// A non-empty string to Visibility, so an empty status line takes no space.
/// </summary>
/// <remarks>
/// Not a <c>BooleanToVisibilityConverter</c>, because there is no boolean here - the binding is a
/// string, and a converter bound to the wrong type fails at load rather than degrading.
/// </remarks>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text && !string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;

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
