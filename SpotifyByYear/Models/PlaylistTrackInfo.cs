using System;

namespace SpotifyByYear.Models;

/// <summary>
/// One entry in a playlist (usually a track; can be an episode or a local file),
/// plus the raw JSON Spotify returned for it.
/// </summary>
public sealed record PlaylistTrackInfo(
    int Position,
    string Type,
    string Name,
    string Artists,
    string Album,
    string? ReleaseDate,
    string? ReleaseDatePrecision,
    bool IsLocal,
    DateTimeOffset? AddedAt,
    string RawJson)
{
    /// <summary>First 4 characters of the release date (dates can be YYYY, YYYY-MM or YYYY-MM-DD).</summary>
    public string? ReleaseYear => ReleaseDate is { Length: >= 4 } date ? date[..4] : null;

    public string Subtitle
    {
        get
        {
            var subtitle = $"{Artists} · {ReleaseYear ?? "no year"}";
            if (IsLocal)
            {
                subtitle += " · local file";
            }
            else if (Type != "track")
            {
                subtitle += $" · {Type}";
            }

            return subtitle;
        }
    }

    public string ReleaseSummary => ReleaseDate is null
        ? "No release date"
        : $"Released {ReleaseDate} (precision: {ReleaseDatePrecision ?? "unknown"}) → year {ReleaseYear}";
}
