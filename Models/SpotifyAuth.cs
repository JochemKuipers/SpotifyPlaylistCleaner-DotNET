using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using static SpotifyAPI.Web.Scopes;

namespace SpotifyPlaylistCleaner_DotNET.Models;


public static class SpotifyAuth
{
    public const string CredentialsPath = "credentials.json";
    private static readonly string? ClientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
    private static readonly EmbedIOAuthServer Server = new(new Uri("http://localhost:8080/callback"), 8080);

    public static async Task<SpotifyClient> Authenticate()
    {
        if (string.IsNullOrEmpty(ClientId))
        {
            throw new NullReferenceException(
              "Please set SPOTIFY_CLIENT_ID via environment variables before starting the program"
            );
        }

        if (File.Exists(CredentialsPath))
        {
            return await StartAndGetClient();
        }
        else
        {
            return await StartAuthenticationAndGetClient();
        }
    }

    private static async Task<SpotifyClient> StartAndGetClient()
    {
        var json = await File.ReadAllTextAsync(CredentialsPath);
        var token = JsonConvert.DeserializeObject<PKCETokenResponse>(json);

        var authenticator = new PKCEAuthenticator(ClientId!, token!);
        authenticator.TokenRefreshed += (sender, tokenResponse) =>
          File.WriteAllText(CredentialsPath, JsonConvert.SerializeObject(tokenResponse));

        var config = SpotifyClientConfig.CreateDefault()
          .WithAuthenticator(authenticator)
          .WithRetryHandler(new SimpleRetryHandler { RetryTimes = 3 });

        var spotify = new SpotifyClient(config);

        return spotify;
    }

    private static async Task<SpotifyClient> StartAuthenticationAndGetClient()
    {
        var (verifier, challenge) = PKCEUtil.GenerateCodes();
        await Server.Start();

        var tcs = new TaskCompletionSource<SpotifyClient>();

        Server.AuthorizationCodeReceived += async (sender, response) =>
        {
            try
            {
                await Server.Stop();
                var token = await new OAuthClient().RequestToken(
              new PKCETokenRequest(ClientId!, response.Code, Server.BaseUri, verifier)
            );

                await File.WriteAllTextAsync(CredentialsPath, JsonConvert.SerializeObject(token));
                var client = await StartAndGetClient();
                tcs.SetResult(client);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        };

        var request = new LoginRequest(Server.BaseUri, ClientId!, LoginRequest.ResponseType.Code)
        {
            CodeChallenge = challenge,
            CodeChallengeMethod = "S256",
            Scope = [UserReadEmail, UserReadPrivate, PlaylistReadPrivate, PlaylistReadCollaborative]
        };

        var uri = request.ToUri();
        try
        {
            BrowserUtil.Open(uri);
        }
        catch (Exception)
        {
            Console.WriteLine("Unable to open URL, manually open: {0}", uri);
        }

        return await tcs.Task;
    }
}
