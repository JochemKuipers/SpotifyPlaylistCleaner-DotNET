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

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null)
                return string.Empty;

            // Handle IList<SimpleArtist> from SpotifyAPI
            if (value is IList<SimpleArtist> simpleArtists)
            {
                return string.Join(Separator, simpleArtists.Select(a => a.Name));
            }
            
            // Handle List<string> from our TrackModel
            if (value is IList<string> artistNames)
            {
                return string.Join(Separator, artistNames);
            }

            return string.Empty;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}