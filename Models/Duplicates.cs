using System;
using System.Collections.Generic;
using System.Linq;
using SpotifyPlaylistCleaner_DotNET.ViewModels;

namespace SpotifyPlaylistCleaner_DotNET.Models;

public class Duplicates(
    List<TrackItemViewModel> trackItems)
{
    private List<DuplicateGroup> GetAndGroupDuplicates()
    {
        try
        {
            var groupedTracks = new Dictionary<string, List<TrackItemViewModel>>();

            foreach (var trackItem in trackItems)
            {
                var cleanedName = Utils.CleanName(trackItem.Name);

                var artistsNormalized = string.Join(",", trackItem.Artists.Select(a => a.Name.ToLower().Trim()));

                var key = $"{cleanedName}|{artistsNormalized}";

                if (!groupedTracks.ContainsKey(key))
                    groupedTracks[key] = [];

                groupedTracks[key].Add(trackItem);
            }

            Console.WriteLine($"Found {groupedTracks.Count} unique tracks");

            var duplicateEntries = groupedTracks.Where(kvp => kvp.Value.Count > 1).ToList();

            return [.. duplicateEntries.Select(kvp => kvp.Value).Select(tracksInGroup => new DuplicateGroup
            { Track = tracksInGroup[0], Tracks = [.. tracksInGroup] })];
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error grouping duplicates: {e}");
            throw;
        }
    }


    private static List<DuplicateGroup> FindExactDuplicates(List<DuplicateGroup> duplicatesGrouped)
    {
        try
        {
            var exactDuplicates = new List<DuplicateGroup>();

            foreach (var duplicate in duplicatesGrouped)
            {
                if (duplicate.Track == null) continue;
                var trackName = duplicate.Track.Name;
                var trackArtists = string.Join(", ", duplicate.Track.Artists.Select(a => a.Name));

                var exactGroup = new List<TrackItemViewModel>();

                for (var i = 0; i < duplicate.Tracks.Count; i++)
                for (var j = i + 1; j < duplicate.Tracks.Count; j++)
                    try
                    {
                        var track1 = duplicate.Tracks[i];
                        var track2 = duplicate.Tracks[j];

                        var sameOriginalName = track1.Name == track2.Name;
                        var sameArtists = string.Join(",", track1.Artists.Select(a => a.Name)) ==
                                          string.Join(",", track2.Artists.Select(a => a.Name));

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

            foreach (var duplicate in exactDuplicates)
            {
                var track = duplicate.Track;
                if (track == null) continue;
                var artistNames = string.Join(", ", track.Artists.Select(a => a.Name));
                Console.WriteLine($"{track.Name} by {artistNames}");
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
        public TrackItemViewModel? Track { get; init; }
        private TrackItemViewModel? OriginalTrack => Tracks.FirstOrDefault();

        public required List<TrackItemViewModel> Tracks { get; init; }

        public int DuplicateCount => Tracks.Count;
        public string? Uri => OriginalTrack?.Uri;

        public string? DisplayImage => OriginalTrack?.DisplayImage;
        public string? DisplayName => OriginalTrack?.Name;
        public string? DisplayArtist => OriginalTrack?.DisplayArtist;
    }
}