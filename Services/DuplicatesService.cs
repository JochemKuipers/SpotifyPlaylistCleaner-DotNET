using System;
using System.Collections.Generic;
using System.Linq;
using SpotifyPlaylistCleaner_DotNET.Models;

namespace SpotifyPlaylistCleaner_DotNET.Services;

public class DuplicatesService : IDuplicatesService
{
    public List<DuplicateGroup> FindDuplicates(IList<TrackModel> tracks)
    {
        if (tracks == null || !tracks.Any())
            throw new ArgumentException("Tracks collection must not be empty", nameof(tracks));

        // First, group tracks by normalized name (case insensitive)
        var duplicatesGrouped = new List<DuplicateGroup>();
        var tracksByNormalizedName = tracks.GroupBy(t => Utils.CleanName(t.Name)?.ToLowerInvariant())
            .Where(g => g.Key != null && g.Count() > 1)
            .ToList();

        foreach (var group in tracksByNormalizedName)
        {
            var groupTracks = group.ToList();
            var duplicateGroup = new DuplicateGroup
            {
                Tracks = groupTracks,
                DisplayName = group.First().Name,
                DisplayArtist = group.First().ArtistNames,
                DisplayImage = group.First().AlbumImageUrl
            };
            duplicatesGrouped.Add(duplicateGroup);
        }

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
                .GroupBy(t => string.Join(",", t.ArtistNames?.ToLowerInvariant()))
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var exactGroup in exactGroups)
            {
                var tracks = exactGroup.ToList();

                // Check if tracks have similar duration
                var durationGroups = new List<List<TrackModel>>();
                foreach (var track in tracks)
                {
                    bool added = false;
                    foreach (var durationGroup in durationGroups)
                    {
                        if (Utils.IsDurationWithinRange(track.DurationMs, durationGroup[0].DurationMs))
                        {
                            durationGroup.Add(track);
                            added = true;
                            break;
                        }
                    }

                    if (!added)
                    {
                        durationGroups.Add([track]);
                    }
                }

                // Add groups with multiple tracks to results
                foreach (var durationGroup in durationGroups.Where(g => g.Count > 1))
                {
                    var duplicateGroup = new DuplicateGroup
                    {
                        Tracks = durationGroup,
                        DisplayName = durationGroup[0].Name,
                        DisplayArtist = durationGroup[0].ArtistNames,
                        DisplayImage = durationGroup[0].AlbumImageUrl
                    };
                    result.Add(duplicateGroup);
                }
            }
        }

        return result;
    }

    public int GetTrackToKeepIndex(DuplicateGroup group)
    {
        // Default to first track
        int indexToKeep = 0;

        // Check for explicit tracks
        bool hasExplicitTracks = group.Tracks.Any(t => t.IsExplicit);
        bool allTracksExplicit = group.Tracks.All(t => t.IsExplicit);

        // If there's a mix of explicit and non-explicit tracks, prioritize keeping an explicit one
        if (hasExplicitTracks && !allTracksExplicit)
        {
            // Find the first explicit track
            for (int i = 0; i < group.Tracks.Count; i++)
            {
                if (group.Tracks[i].IsExplicit)
                {
                    indexToKeep = i;
                    break;
                }
            }
        }

        return indexToKeep;
    }
}