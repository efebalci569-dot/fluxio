using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Fluxio.Desktop.Controls;

/// <summary>true → Visible (or Collapsed when Invert=true).</summary>
public sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ Invert;
}

/// <summary>null/empty → Collapsed (or Visible when Invert=true).</summary>
public sealed class NullToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var empty = value is null || (value is string s && s.Length == 0) || (value is int i && i == 0);
        return empty ^ Invert ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible when the value's string form equals ConverterParameter (comma separated list allowed).</summary>
public sealed class EqualsToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? string.Empty;
        var match = (parameter?.ToString() ?? string.Empty).Split(',').Any(p => string.Equals(p.Trim(), text, StringComparison.Ordinal));
        return match ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>[message, isError] → Visible for the matching notice (error or success).</summary>
public sealed class MessageVisibility : IMultiValueConverter
{
    public static readonly MessageVisibility Error = new() { ForError = true };
    public static readonly MessageVisibility Success = new() { ForError = false };

    public bool ForError { get; init; }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var hasMessage = values.Length > 0 && values[0] is string { Length: > 0 };
        var isError = values.Length > 1 && values[1] is true;
        return hasMessage && isError == ForError ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}

/// <summary>Two-way bool binding for radio buttons bound to an enum/string value.</summary>
public sealed class EqualsToBool : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null)
        {
            return Binding.DoNothing;
        }

        return targetType.IsEnum ? Enum.Parse(targetType, parameter.ToString()!) : parameter.ToString()!;
    }
}
