using System;
using System.Globalization;
using System.Windows.Data;

namespace Casino.Views
{
    public sealed class BooleanToContentConverter : IValueConverter
    {
        // ConverterParameter: "TextoSiTrue,TextoSiFalse"
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var param = parameter as string ?? string.Empty;
            var parts = param.Split(',');
            var trueText = parts.Length > 0 ? parts[0] : "True";
            var falseText = parts.Length > 1 ? parts[1] : "False";

            var b = value is bool v && v;
            return b ? trueText : falseText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}