using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using SpotifyAPI.Web;

namespace SpotifyPlaylistCleaner_DotNET.Converters
{
    public class ArtistListToStringConverter : IValueConverter
    {
        public string Separator { get; set; } = ", ";

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is IEnumerable<SimpleArtist> artists)
            {
                // Extract artist names and join them with the separator
                return string.Join(Separator, artists.Select(a => a.Name));
            }
            
            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // We don't need to convert back for this use case
            throw new NotImplementedException();
        }
    }
}