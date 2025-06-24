using System;
using System.Collections.Generic;
using System.Linq;
using SpotifyPlaylistCleaner_DotNET.Models;

namespace SpotifyPlaylistCleaner_DotNET.Services;

public class DuplicatesService : IDuplicatesService
{
    public List<DuplicateGroup> FindDuplicates(IList<TrackModel>? tracks)
    {
        if (tracks == null || !tracks.Any())
            throw new ArgumentException("Tracks collection must not be empty", nameof(tracks));

        // First, group tracks by normalized name (case-insensitive)
        var tracksByNormalizedName = tracks.GroupBy(t => Utils.CleanName(t.Name).ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .ToList();

        var duplicatesGrouped = (from @group in tracksByNormalizedName let groupTracks = @group.ToList() select new DuplicateGroup { Tracks = groupTracks, DisplayName = @group.First().Name, DisplayArtist = @group.First().ArtistNames, DisplayImage = @group.First().AlbumImageUrl }).ToList();

        // Then, filter for exact duplicates with same artists and similar duration
        var exactDuplicates = FindExactDuplicates(duplicatesGrouped);
        return exactDuplicates;
    }

    private static List<DuplicateGroup> FindExactDuplicates(List<DuplicateGroup> duplicatesGrouped)
    {
        var result = new List<DuplicateGroup>();

        foreach (var group in duplicatesGrouped)
        {
            // Group by artist and check duration
            var exactGroups = group.Tracks
                .GroupBy(t => string.Join(",", t.ArtistNames.ToLowerInvariant()))
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var exactGroup in exactGroups)
            {
                var tracks = exactGroup.ToList();

                // Check if tracks have similar duration
                var durationGroups = new List<List<TrackModel>>();
                foreach (var track in tracks)
                {
                    var added = false;
                    foreach (var durationGroup in durationGroups.Where(durationGroup => Utils.IsDurationWithinRange(track.DurationMs, durationGroup[0].DurationMs)))
                    {
                        durationGroup.Add(track);
                        added = true;
                        break;
                    }

                    if (!added)
                    {
                        durationGroups.Add([track]);
                    }
                }

                // Add groups with multiple tracks to results
                result.AddRange(durationGroups.Where(g => g.Count > 1).Select(durationGroup => new DuplicateGroup { Tracks = durationGroup, DisplayName = durationGroup[0].Name, DisplayArtist = durationGroup[0].ArtistNames, DisplayImage = durationGroup[0].AlbumImageUrl }));
            }
        }

        return result;
    }

    public int GetTrackToKeepIndex(DuplicateGroup group)
    {
        // Default to first track
        var indexToKeep = 0;

        // First, check if there's a mix of local and Spotify tracks
        var hasLocalTracks = group.Tracks.Any(t => t.IsLocal);
        var hasSpotifyTracks = group.Tracks.Any(t => !t.IsLocal);

        // If there's a mix, prioritize Spotify tracks over local files
        if (hasLocalTracks && hasSpotifyTracks)
        {
            // Find the first Spotify track (non-local)
            for (var i = 0; i < group.Tracks.Count; i++)
            {
                if (group.Tracks[i].IsLocal) continue;
                indexToKeep = i;
                break;
            }
        }

        // Then check for explicit tracks, but only among Spotify tracks
        // (local tracks don't have reliable IsExplicit information)
        var spotifyTracks = group.Tracks.Where(t => !t.IsLocal).ToList();

        if (spotifyTracks.Count <= 0) return indexToKeep;
        {
            var hasExplicitTracks = spotifyTracks.Any(t => t.IsExplicit);
            var allTracksExplicit = spotifyTracks.All(t => t.IsExplicit);

            // If there's a mix of explicit and non-explicit tracks, prioritize keeping an explicit one
            if (!hasExplicitTracks || allTracksExplicit) return indexToKeep;
            // Find the first explicit track among the Spotify tracks
            for (var i = 0; i < group.Tracks.Count; i++)
            {
                if (group.Tracks[i].IsLocal || !group.Tracks[i].IsExplicit) continue;
                indexToKeep = i;
                break;
            }
        }

        return indexToKeep;
    }
}