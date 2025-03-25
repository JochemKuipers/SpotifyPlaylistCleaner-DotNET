using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SpotifyPlaylistCleaner_DotNET.Converters
{
    public class ItemIndexConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 2 && values[0] is IList items && values[1] != null)
            {
                int index = items.IndexOf(values[1]) + 1; // +1 for 1-based indexing
                return index.ToString("D2"); // Format as 01, 02, etc.
            }
            return "??";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}