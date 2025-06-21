using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Newtonsoft.Json;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using static SpotifyAPI.Web.Scopes;

namespace SpotifyPlaylistCleaner_DotNET.Models;

public static class SpotifyAuth
{
    public const string CredentialsPath = "credentials.json";
    private const string ClientIdStoragePath = "spotify_client_id.dat";
    private static readonly EmbedIOAuthServer Server = new(new Uri("http://127.0.0.1:3000/callback"), 3000);

    // Updated EncryptString method to handle cross-platform compatibility
    private static string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use DPAPI for Windows
                byte[] encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    null,
                    DataProtectionScope.CurrentUser);

                return Convert.ToBase64String(encryptedBytes);
            }
            else
            {
                // Use AES for non-Windows platforms
                using var aes = Aes.Create();
                aes.GenerateKey();
                aes.GenerateIV();

                using var encryptor = aes.CreateEncryptor();
                byte[] encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                // Combine key, IV, and encrypted data for storage
                return Convert.ToBase64String(aes.Key) + ":" + Convert.ToBase64String(aes.IV) + ":" + Convert.ToBase64String(encryptedBytes);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error encrypting data: {ex.Message}");
            return string.Empty;
        }
    }

    // Updated DecryptString method to handle cross-platform compatibility
    private static string DecryptString(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return string.Empty;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use DPAPI for Windows
                byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
                byte[] plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(plainBytes);
            }
            else
            {
                // Use AES for non-Windows platforms
                var parts = encryptedText.Split(':');
                if (parts.Length != 3)
                    throw new FormatException("Invalid encrypted text format.");

                byte[] key = Convert.FromBase64String(parts[0]);
                byte[] iv = Convert.FromBase64String(parts[1]);
                byte[] encryptedBytes = Convert.FromBase64String(parts[2]);

                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;

                using var decryptor = aes.CreateDecryptor();
                byte[] plainBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

                return Encoding.UTF8.GetString(plainBytes);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error decrypting data: {ex.Message}");
            return string.Empty;
        }
    }

    private static string? GetClientId()
    {
        string? clientId = GetClientIdFromStorage();
        
        if (string.IsNullOrEmpty(clientId))
        {
            clientId = PromptUserForClientId().GetAwaiter().GetResult();
            
            if (!string.IsNullOrEmpty(clientId))
            {
                SaveClientIdToStorage(clientId);
            }
        }
        
        return clientId;
    }

    private static string? GetClientIdFromStorage()
    {
        try
        {
            if (File.Exists(ClientIdStoragePath))
            {
                string encryptedClientId = File.ReadAllText(ClientIdStoragePath);
                return DecryptString(encryptedClientId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading client ID from storage: {ex.Message}");
        }

        return null;
    }

    private static void SaveClientIdToStorage(string clientId)
    {
        try
        {
            string encryptedClientId = EncryptString(clientId);
            File.WriteAllText(ClientIdStoragePath, encryptedClientId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving client ID to storage: {ex.Message}");
        }
    }



    private static async Task<string?> PromptUserForClientId()
    {
        var taskCompletionSource = new TaskCompletionSource<string?>();

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new Window
            {
                Title = "Spotify Client ID Required",
                Width = 450,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                CanResize = false
            };

            var layout = new StackPanel
            {
                Margin = new Avalonia.Thickness(20)
            };

            layout.Children.Add(new TextBlock
            {
                Text = "Please enter your Spotify Client ID",
                Margin = new Avalonia.Thickness(0, 0, 0, 10)
            });

            layout.Children.Add(new TextBlock
            {
                Text = "You can get one from https://developer.spotify.com/dashboard",
                Margin = new Avalonia.Thickness(0, 0, 0, 10)
            });

            var clientIdTextBox = new TextBox
            {
                Watermark = "Client ID",
                Margin = new Avalonia.Thickness(0, 0, 0, 20)
            };
            layout.Children.Add(clientIdTextBox);

            var buttonsPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 10
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 100,
                IsDefault = true
            };

            buttonsPanel.Children.Add(cancelButton);
            buttonsPanel.Children.Add(okButton);
            layout.Children.Add(buttonsPanel);

            dialog.Content = layout;

            cancelButton.Click += (s, e) =>
            {
                taskCompletionSource.SetResult(null);
                dialog.Close();
            };

            okButton.Click += (s, e) =>
            {
                taskCompletionSource.SetResult(clientIdTextBox.Text?.Trim());
                dialog.Close();
            };

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                await dialog.ShowDialog(desktopLifetime.MainWindow!);
            }
            else
            {
                throw new InvalidOperationException("Application lifetime is not a desktop style application lifetime.");
            }
        });

        return await taskCompletionSource.Task;
    }

    private static readonly string? ClientId = GetClientId();

    public static async Task<SpotifyClient> Authenticate()
    {
        if (string.IsNullOrEmpty(ClientId))
        {
            throw new NullReferenceException(
                "Please provide your Spotify Client ID to use this application."
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
            Scope = [UserReadPrivate, PlaylistReadPrivate, PlaylistReadCollaborative, PlaylistModifyPublic, PlaylistModifyPrivate, UserLibraryRead, UserLibraryModify]
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
