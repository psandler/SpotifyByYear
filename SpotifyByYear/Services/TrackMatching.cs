using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SpotifyByYear.Services;

/// <summary>Fuzzy title/artist comparison for matching the same song across Spotify and MusicBrainz.</summary>
public static partial class TrackMatching
{
    private const string VersionWords =
        @"remaster|remastered|version|mono|stereo|edit|mix|remix|single|deluxe|anniversary|bonus|live|demo|acoustic|recorded|from|feat|ft";

    // Last " - ..." segment containing a version word, e.g. "Rich Girl - 2003 Remaster".
    [GeneratedRegex(@"\s+-\s+(?:(?!\s-\s).)*\b(" + VersionWords + @")\b(?:(?!\s-\s).)*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();

    // Bracketed version text, e.g. "(Remastered 2009)" or "[Live]".
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*\b(" + VersionWords + @")\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex VersionParenthetical();

    // MusicBrainz disambiguations for recordings that aren't the original studio version.
    [GeneratedRegex(@"\b(live|demo|instrumental|karaoke|remix|rehearsal|acoustic|a cappella|cover)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AlternateVersion();

    /// <summary>Removes remaster/live/version decorations from a title.</summary>
    public static string CleanTitle(string title)
    {
        var cleaned = title;
        string previous;
        do
        {
            previous = cleaned;
            cleaned = VersionSuffix().Replace(cleaned, "");
            cleaned = VersionParenthetical().Replace(cleaned, "");
        } while (cleaned != previous);

        cleaned = cleaned.Trim();
        return cleaned.Length > 0 ? cleaned : title.Trim();
    }

    public static bool HasVersionDecoration(string title) => CleanTitle(title) != title.Trim();

    /// <summary>Lowercase, no accents or punctuation, "&amp;" → "and", single spaces.</summary>
    public static string Normalize(string value)
    {
        var decomposed = value.Replace("&", " and ").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c is '\'' or '’')
            {
                continue; // drop accents; "don't" → "dont"
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    public static bool TitlesMatch(string a, string b) =>
        Normalize(CleanTitle(a)) is { Length: > 0 } na && na == Normalize(CleanTitle(b));

    /// <summary>Equal, or one contains the other ("Hall &amp; Oates" vs "Daryl Hall &amp; John Oates" won't match; "The Beatles" vs "Beatles" will).</summary>
    public static bool ArtistsMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length < 3 || nb.Length < 3)
        {
            return na == nb;
        }

        return na == nb || na.Contains(nb, StringComparison.Ordinal) || nb.Contains(na, StringComparison.Ordinal);
    }

    public static bool IsAlternateVersion(string? disambiguation) =>
        !string.IsNullOrEmpty(disambiguation) && AlternateVersion().IsMatch(disambiguation);
}
