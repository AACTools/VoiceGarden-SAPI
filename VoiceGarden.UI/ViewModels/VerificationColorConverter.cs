using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VoiceGarden.UI.ViewModels;

/// <summary>
/// Green for valid, red for invalid, transparent for empty.
/// </summary>
public class VerificationColorConverter : IValueConverter
{
    public static readonly VerificationColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            if (s.StartsWith("✓")) return Brushes.Green;
            if (s.StartsWith("✗")) return Brushes.Red;
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
