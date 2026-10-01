using System;
using System.Globalization;
using System.Windows.Data;

namespace GamingLiveTranslator.Utilities;

/// <summary>
/// Converts boolean values to user-friendly status text (e.g. ON / OFF, Enabled / Disabled).
/// Parameter can be specified as "TrueText|FalseText" (default: "ON|OFF").
/// </summary>
public class BooleanToStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isTrue = value is true;
        if (parameter is string paramStr && paramStr.Contains('|'))
        {
            var parts = paramStr.Split('|');
            return isTrue ? parts[0] : parts[1];
        }

        return isTrue ? "ON" : "OFF";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str && parameter is string paramStr && paramStr.Contains('|'))
        {
            var parts = paramStr.Split('|');
            return string.Equals(str, parts[0], StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(value?.ToString(), "ON", StringComparison.OrdinalIgnoreCase);
    }
}
