using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlaybackPresent.Converters;

//public class EnumToBoolConverter : IValueConverter
//{
//   // ViewModel -> View: Does the current Enum match the Button's value?
//   public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
//   {
//      return value?.Equals(parameter);
//   }

//   // View -> ViewModel: If this button was checked (true), return its Enum value
//   public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
//   {
//      return value is true ? parameter : AvaloniaProperty.UnsetValue;
//   }
//}