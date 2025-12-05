using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Kairo.Utils.Logger;
using Kairo.ViewModels;

namespace Kairo.Utils.Converters
{
    public class LogLineToControlConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is LogEntry entry)
            {
                var tb = LogPreProcess.ToColoredTextBlock(entry.Type, entry.Line);
                tb.Tag = entry;
                return tb;
            }
            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
