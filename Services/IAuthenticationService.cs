using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace SpotifyPlaylistCleaner_DotNET.Services;

public interface IAuthenticationService
{
    Task<SpotifyClient> Authenticate();
    Task<bool> IsAuthenticated();
    Task<string> GetUserDisplayName();
}