using System;
using System.Text.RegularExpressions;

namespace SpotifyPlaylistCleaner_DotNET.Models;

public static partial class Utils
{
    public static string CleanName(string songName)
    {
        try
        {
            songName = MyRegex().IsMatch(songName)
                ? MyRegex2().Split(songName)[0].Trim()
                : MyRegex1().Replace(songName, "").Trim();
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
    private static partial Regex MyRegex();

    [GeneratedRegex(@"\s*(feat\.|with\.)\s.*$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MyRegex1();

    [GeneratedRegex(@"[\[(].*", RegexOptions.None)]
    private static partial Regex MyRegex2();
}