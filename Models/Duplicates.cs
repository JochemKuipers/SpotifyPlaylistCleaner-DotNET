using System;
using System.Collections.Generic;
using System.Linq;
using SpotifyAPI.Web;
using SpotifyPlaylistCleaner_DotNET.ViewModels;

namespace SpotifyPlaylistCleaner_DotNET.Models;

public class Duplicates(
    List<TrackItemViewModel> trackItems,
    Action<FullTrack> deleteTrackAction,
    Action<string> updateStatusAction)
{
    private readonly Action<FullTrack> _deleteTrackAction = deleteTrackAction;

    private readonly Action<string> _updateStatusAction = updateStatusAction;

    private void DeleteTrack(FullTrack track)
    {
        _deleteTrackAction(track);
    }

    private List<DuplicateGroup> GetAndGroupDuplicates()
    {
        try
        {
            // Group tracks by cleaned name and artists
            var groupedTracks = new Dictionary<string, List<TrackItemViewModel>>();

            // Step 1: Populate the dictionary
            foreach (var trackItem in trackItems)
            {
                // Clean track name
                var cleanedName = Utils.CleanName(trackItem.Name);

                // Create a normalized artist string for comparison
                var artistsNormalized = string.Join(",", trackItem.Artists.Select(a => a.Name.ToLower().Trim()));

                // Combine name and artists into a single key
                var key = $"{cleanedName}|{artistsNormalized}";

                if (!groupedTracks.ContainsKey(key))
                    groupedTracks[key] = [];

                groupedTracks[key].Add(trackItem);
            }

            Console.WriteLine($"Found {groupedTracks.Count} unique tracks");

            // Step 2: Find entries with multiple tracks (duplicates)
            var duplicateEntries = new List<KeyValuePair<string, List<TrackItemViewModel>>>();

            foreach (var kvp in groupedTracks)
                if (kvp.Value.Count > 1)
                    duplicateEntries.Add(kvp);

            Console.WriteLine($"Found {duplicateEntries.Count} duplicates");

            // Step 3: Create duplicate groups
            var duplicateGroups = new List<DuplicateGroup>();

            foreach (var kvp in duplicateEntries)
            {
                var tracksInGroup = kvp.Value;

                var group = new DuplicateGroup
                {
                    Track = tracksInGroup[0],
                    Tracks = new List<TrackItemViewModel>(tracksInGroup)
                };

                duplicateGroups.Add(group);
            }

            return duplicateGroups;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error grouping duplicates: {e}");
            throw;
        }
    }


    private List<DuplicateGroup> FindExactDuplicates(List<DuplicateGroup> duplicatesGrouped)
    {
        try
        {
            var exactDuplicates = new List<DuplicateGroup>();

            foreach (var duplicate in duplicatesGrouped)
            {
                if (duplicate.Track == null) continue;
                var trackName = duplicate.Track.Name;
                var trackArtists = string.Join(", ", duplicate.Track.Artists.Select(a => a.Name));

                // Check for exact duplicates within this group
                var exactGroup = new List<TrackItemViewModel>();

                for (var i = 0; i < duplicate.Tracks.Count; i++)
                for (var j = i + 1; j < duplicate.Tracks.Count; j++)
                    try
                    {
                        var track1 = duplicate.Tracks[i];
                        var track2 = duplicate.Tracks[j];

                        // Compare all attributes to find exact duplicates
                        var sameOriginalName = track1.Name == track2.Name;
                        var sameArtists = string.Join(",", track1.Artists.Select(a => a.Name)) ==
                                          string.Join(",", track2.Artists.Select(a => a.Name));

                        // Compare durations using the utility method
                        var durationClose = Utils.IsDurationWithinRange(
                            track1.DurationMs,
                            track2.DurationMs);

                        if (!sameOriginalName || !sameArtists || !durationClose) continue;
                        if (!exactGroup.Contains(track1))
                            exactGroup.Add(track1);
                        if (!exactGroup.Contains(track2))
                            exactGroup.Add(track2);
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"Error finding exact duplicates: {e} on {trackName} - {trackArtists}");
                        throw;
                    }

                if (exactGroup.Count > 0)
                    exactDuplicates.Add(new DuplicateGroup
                    {
                        Track = duplicate.Track,
                        Tracks = exactGroup
                    });
            }

            Console.WriteLine($"Found {exactDuplicates.Count} exact duplicates");

            // Print details about the exact duplicates
            foreach (var duplicate in exactDuplicates)
            {
                var track = duplicate.Track;
                if (track != null)
                {
                    var artistNames = string.Join(", ", track.Artists.Select(a => a.Name));
                    Console.WriteLine($"{track.Name} by {artistNames}");
                }

                for (var i = 0; i < duplicate.Tracks.Count; i++)
                {
                    var trackItem = duplicate.Tracks[i];
                    var duration = TimeSpan.FromMilliseconds(trackItem.DurationMs).ToString(@"mm\:ss");
                    Console.WriteLine($"\t{i + 1}. {trackItem.Name} - {trackItem.Uri} : {duration}");
                }
            }

            return exactDuplicates;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error finding exact duplicates: {e}");
            throw;
        }
    }

    public List<DuplicateGroup> FindAllDuplicates()
    {
        var potentialDuplicates = GetAndGroupDuplicates();
        return FindExactDuplicates(potentialDuplicates);
    }


    public class DuplicateGroup : ViewModelBase, MainWindowViewModel.ITreeNode
    {
        public string? Name => GroupName;
        public string Description => $"{ArtistNames} - {DuplicateCount} duplicates";
        public bool IsExpanded { get; set; } = true;
        public bool IsSelected { get; set; } = false;
        public bool IsEnabled { get; set; } = true;
        public bool IsChecked { get; set; } = false;

        public Action<DuplicateGroup>? OnDeleteAction { get; set; }
        public TrackItemViewModel? Track { get; init; }
        private TrackItemViewModel? OriginalTrack => Tracks.FirstOrDefault();

        public required List<TrackItemViewModel> Tracks { get; init; }

        private string? GroupName => OriginalTrack?.Name;
        private string? ArtistNames => OriginalTrack?.ArtistNames;
        public int DuplicateCount => Tracks.Count;

        public string? DisplayImage => OriginalTrack?.DisplayImage;
        public string? Uri => OriginalTrack?.Uri;
        public string? DisplayName => OriginalTrack?.Name;
        public string? DisplayArtist => OriginalTrack?.DisplayArtist;

        public void DeleteTrack()
        {
            if (OriginalTrack != null)
                DeleteTrack();
            OnDeleteAction?.Invoke(this);
        }
    }
}