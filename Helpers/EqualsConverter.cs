using System.Globalization;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;

namespace WorkCheck.Helpers;

// Binds a group of RadioButtons to one string property: a button is checked when the value equals its
// ConverterParameter, and checking it writes the parameter back
public class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}
