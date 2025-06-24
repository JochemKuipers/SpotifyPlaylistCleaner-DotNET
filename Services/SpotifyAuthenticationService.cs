using System;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using SpotifyPlaylistCleaner_DotNET.Models;

namespace SpotifyPlaylistCleaner_DotNET.Services;

public class SpotifyAuthenticationService : IAuthenticationService
{
    private SpotifyClient? _spotifyClient;

    public async Task<SpotifyClient> Authenticate()
    {
        if (_spotifyClient != null)
            return _spotifyClient;

        try
        {
            _spotifyClient = await SpotifyAuth.Authenticate();
            return _spotifyClient;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Authentication failed: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> IsAuthenticated()
    {
        if (_spotifyClient == null)
            return false;

        try
        {
            // Try to get current user to verify the client is authenticated
            await _spotifyClient.UserProfile.Current();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GetUserDisplayName()
    {
        if (_spotifyClient == null)
            throw new InvalidOperationException("Not authenticated");

        var user = await _spotifyClient.UserProfile.Current();
        return user.DisplayName;
    }
}