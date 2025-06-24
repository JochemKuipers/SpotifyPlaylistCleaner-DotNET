using SpotifyAPI.Web;

namespace SpotifyPlaylistCleaner_DotNET.Models;

public class PlaylistModel
{
    public string Id { get; private init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsLikedSongs { get; private init; }

    public static PlaylistModel FromFullPlaylist(FullPlaylist playlist)
    {
        return new PlaylistModel
        {
            Id = playlist.Id ?? string.Empty,
            Name = playlist.Name ?? string.Empty,
            Description = playlist.Description ?? string.Empty,
            ImageUrl = playlist.Images?.Count > 0 ? playlist.Images[0].Url : string.Empty,
            IsLikedSongs = playlist.Id == "liked_songs_virtual"
        };
    }

    public static PlaylistModel CreateLikedSongsPlaylist(string userId)
    {
        return new PlaylistModel
        {
            Id = "liked_songs_virtual",
            Name = "Liked Songs",
            Description = "Songs you've liked on Spotify",
            ImageUrl = "https://t.scdn.co/images/3099b3803ad9496896c43f22fe9be8c4.png",
            IsLikedSongs = true
        };
    }
}