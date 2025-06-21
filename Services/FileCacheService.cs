using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace SpotifyPlaylistCleaner_DotNET.Services;

public class FileCacheService : ICacheService
{
    private readonly string _cacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotifyPlaylistCleaner");

    private readonly TimeSpan _cacheTtl = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };

    public FileCacheService()
    {
        if (!Directory.Exists(_cacheFolder))
            Directory.CreateDirectory(_cacheFolder);
    }

    public bool IsCacheEnabled { get; set; } = true;

    public async Task<IList<FullTrack>?> LoadTracksFromCacheAsync(string playlistId, CancellationToken cancellationToken = default)
    {
        if (!IsCacheEnabled)
            return null;

        var cacheFile = Path.Combine(_cacheFolder, $"playlist_{playlistId}.json");

        if (!File.Exists(cacheFile))
            return null;

        var fileInfo = new FileInfo(cacheFile);
        var cacheAge = DateTime.Now - fileInfo.LastWriteTime;
        if (cacheAge > _cacheTtl)
            return null;

        try
        {
            await using var fs = File.OpenRead(cacheFile);
            var cachedTracks = await JsonSerializer.DeserializeAsync<List<FullTrack>>(fs, JsonOptions, cancellationToken);
            return cachedTracks;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache loading error for {cacheFile}: {ex.Message}");
            try
            {
                File.Delete(cacheFile);
            }
            catch
            {
                // Ignore deletion errors
            }
        }

        return null;
    }

    public async Task<IList<FullTrack>?> LoadLikedTracksFromCacheAsync(CancellationToken cancellationToken = default)
    {
        if (!IsCacheEnabled)
            return null;

        var cacheFile = Path.Combine(_cacheFolder, "liked_songs.json");

        if (!File.Exists(cacheFile))
            return null;

        var fileInfo = new FileInfo(cacheFile);
        var cacheAge = DateTime.Now - fileInfo.LastWriteTime;
        if (cacheAge > _cacheTtl)
            return null;

        try
        {
            await using var fs = File.OpenRead(cacheFile);
            var cachedTracks = await JsonSerializer.DeserializeAsync<List<FullTrack>>(fs, JsonOptions, cancellationToken);
            return cachedTracks;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache loading error for {cacheFile}: {ex.Message}");
            try
            {
                File.Delete(cacheFile);
            }
            catch
            {
                // Ignore deletion errors
            }
        }

        return null;
    }

    public async Task SaveTracksToCacheAsync(string playlistId, IList<FullTrack> tracks, CancellationToken cancellationToken = default)
    {
        if (!IsCacheEnabled)
            return;

        try
        {
            var cacheFile = Path.Combine(_cacheFolder, $"playlist_{playlistId}.json");
            await using var fs = File.Create(cacheFile);
            await JsonSerializer.SerializeAsync(fs, tracks, JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache saving error: {ex.Message}");
        }
    }

    public async Task SaveLikedTracksToCacheAsync(IList<FullTrack> tracks, CancellationToken cancellationToken = default)
    {
        if (!IsCacheEnabled)
            return;

        try
        {
            var cacheFile = Path.Combine(_cacheFolder, "liked_songs.json");
            await using var fs = File.Create(cacheFile);
            await JsonSerializer.SerializeAsync(fs, tracks, JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Cache saving error: {ex.Message}");
        }
    }

    public void ClearCache()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_cacheFolder, "*.json"))
                File.Delete(file);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error clearing cache: {ex.Message}");
        }
    }
}