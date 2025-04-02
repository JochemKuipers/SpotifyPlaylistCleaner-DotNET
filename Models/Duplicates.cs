using System;
using System.Collections.Generic;
using SpotifyAPI.Web;
using SpotifyPlaylistCleaner_DotNET.ViewModels;
using Enumerable = System.Linq.Enumerable;

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
            var groupedTracks =
                new Dictionary<(string CleanedName, IList<SimpleArtist> Artists), List<TrackItemViewModel>>();

            foreach (var trackItem in trackItems)
            {
                // Clean track name using existing Utils.CleanName method
                var cleanedName = Utils.CleanName(trackItem.Name);

                // Join artist names as a string for the key
                var artistsKey = trackItem.Artists;

                var key = (CleanedName: cleanedName, Artists: artistsKey);

                if (!groupedTracks.ContainsKey(key))
                    groupedTracks[key] = [];

                groupedTracks[key].Add(trackItem);
            }

            // Log grouping info
            foreach (var entry in groupedTracks)
            {
                Console.WriteLine($"{entry.Key.CleanedName} - {entry.Key.Artists}");
                foreach (var trackItem in entry.Value)
                    Console.WriteLine($"\t{trackItem.Name} - {trackItem.Track.Uri}");
            }

            Console.WriteLine($"Found {groupedTracks.Count} unique tracks");

            // Create duplicate groups (only for items with multiple tracks)
            var duplicatesGrouped = Enumerable.ToList(Enumerable.Select(
                Enumerable.Where(groupedTracks, entry => entry.Value.Count > 1), entry => new DuplicateGroup
                {
                    Track = entry.Value[0],
                    Tracks = [..entry.Value]
                }));

            // Log duplicate information
            foreach (var duplicate in duplicatesGrouped)
            {
                var firstTrack = duplicate.Track;
                var artistNames = string.Join(", ", Enumerable.Select(firstTrack.Artists, a => a.Name));
                var albumName = firstTrack.Track.Album.Name;

                Console.WriteLine($"{firstTrack.Name} by {artistNames} ({albumName})");

                for (var i = 0; i < duplicate.Tracks.Count; i++)
                {
                    var trackItem = duplicate.Tracks[i];
                    var duration = trackItem.Duration;
                    Console.WriteLine($"\t{i + 1}. {trackItem.Name} - {trackItem.Track.Uri} : {duration}");
                }
            }

            Console.WriteLine($"Found {duplicatesGrouped.Count} duplicates");
            return duplicatesGrouped;
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
                var trackName = duplicate.Track.Name;
                var trackArtists = string.Join(", ", Enumerable.Select(duplicate.Track.Artists, a => a.Name));

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
                        var sameArtists = string.Join(",", Enumerable.Select(track1.Artists, a => a.Name)) ==
                                          string.Join(",", Enumerable.Select(track2.Artists, a => a.Name));
                        var sameAlbum = track1.Album.Name == track2.Album.Name;

                        // Compare durations using the utility method
                        var durationClose = Utils.IsDurationWithinRange(
                            track1.DurationMs,
                            track2.DurationMs);

                        if (!sameOriginalName || !sameArtists || !sameAlbum || !durationClose) continue;
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
                var artistNames = string.Join(", ", Enumerable.Select(track.Artists, a => a.Name));
                Console.WriteLine($"{track.Name} by {artistNames}");

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

    private class DuplicateGroup
    {
        public required TrackItemViewModel Track { get; init; }
        public required List<TrackItemViewModel> Tracks { get; init; }
    }
}