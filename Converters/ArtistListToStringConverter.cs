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
            return value switch
            {
                null => string.Empty,
                // Handle IList<SimpleArtist> from SpotifyAPI
                IList<SimpleArtist> simpleArtists => string.Join(Separator, simpleArtists.Select(a => a.Name)),
                // Handle List<string> from our TrackModel
                IList<string> artistNames => string.Join(Separator, artistNames),
                _ => string.Empty
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}