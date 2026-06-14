using System;
using System.Globalization;
using System.Windows.Data;

namespace OutWit.Render.ThreeDsMax.Plugin.UI.Converters;

/// <summary>
/// Binds a RadioButton's <c>IsChecked</c> to one value of an enum: returns true when the bound enum
/// equals the ConverterParameter, and on check writes that enum value back. Lets the View drive enum
/// selections (output axis, animation result, settings tab) with no code-behind.
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    #region IValueConverter

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Only the radio that just became checked writes back; the unchecked one yields Binding.DoNothing.
        if (value is true && parameter is not null)
            return Enum.Parse(targetType, parameter.ToString()!);

        return Binding.DoNothing;
    }

    #endregion
}
