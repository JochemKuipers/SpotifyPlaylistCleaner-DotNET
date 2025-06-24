using System;
using System.Text.RegularExpressions;

namespace SpotifyPlaylistCleaner_DotNET.Models;

public static partial class Utils
{
    public static string CleanName(string? songName)
    {
        try
        {
            songName = ContainsFeatOrWithInBracketsOrParenthesesRegex().IsMatch(songName!)
                ? FeatOrWithSuffixRegex().Split(songName!)[0].Trim()
                : BracketsOrParenthesesContentRegex().Replace(songName!, "").Trim();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error cleaning name: {e}");
            throw;
        }

        return songName;
    }

    public static bool IsDurationWithinRange(int duration1, int duration2, int rangeSeconds = 5)
    {
        try
        {
            return Math.Abs(duration1 - duration2) <= rangeSeconds * 1000;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Error comparing durations: {e}");
            return false;
        }
    }

    [GeneratedRegex(@"[\[(].*?(feat\.?|with\.?)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex ContainsFeatOrWithInBracketsOrParenthesesRegex();

    [GeneratedRegex(@"\s*(feat\.|with\.)\s.*$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex FeatOrWithSuffixRegex();

    [GeneratedRegex(@"[\[(].*", RegexOptions.None)]
    private static partial Regex BracketsOrParenthesesContentRegex();
}