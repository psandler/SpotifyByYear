using System;
using System.Collections.Generic;

namespace SpotifyByYear.Models;

/// <summary>Cached original-release-year lookups for one Spotify track (stored in release-years.json).</summary>
public sealed class ReleaseYearEntry
{
    public string TrackId { get; set; } = "";
    public string? Isrc { get; set; }
    public string Title { get; set; } = "";
    public string Artists { get; set; } = "";
    public string? AlbumReleaseDate { get; set; }
    public string? AlbumType { get; set; }

    /// <summary>The title marks this copy as live; the live recording's date is wanted, not the studio original's.</summary>
    public bool IsLiveVersion { get; set; }

    /// <summary>Year written in a live title ("… / 2007"), if any.</summary>
    public int? TitleYear { get; set; }

    /// <summary>Matching-logic version that produced this entry; older entries are looked up again.</summary>
    public int LogicVersion { get; set; }

    public SourceResult? MusicBrainz { get; set; }
    public SourceResult? SpotifySearch { get; set; }
}

/// <summary>Result of one lookup source. <see cref="Year"/> null with no <see cref="Error"/> means "no match".</summary>
public sealed class SourceResult
{
    public DateTimeOffset CheckedAt { get; set; }
    public int? Year { get; set; }

    /// <summary>How the match was found, e.g. "isrc" or "search".</summary>
    public string? Method { get; set; }

    /// <summary>MusicBrainz recording ID or Spotify album ID of the chosen match.</summary>
    public string? MatchId { get; set; }

    public string? MatchDescription { get; set; }

    /// <summary>Short descriptions of the candidates considered, earliest first.</summary>
    public List<string> Candidates { get; set; } = [];

    /// <summary>Set when the lookup failed (network, rate limit); failed lookups are retried.</summary>
    public string? Error { get; set; }
}

/// <summary>A hand-entered year from year-overrides.json; always wins.</summary>
public sealed class YearOverride
{
    public int Year { get; set; }
    public string? Note { get; set; }
}

/// <summary>A cache entry plus any override, with the rules for picking the year.</summary>
public sealed record ResolvedReleaseYear(ReleaseYearEntry Entry, YearOverride? Override)
{
    public int? Year => Pick().Year;

    public string Source => Pick().Source;

    public bool SourcesDisagree =>
        Entry.MusicBrainz?.Year is int mb && Entry.SpotifySearch?.Year is int sp && mb != sp;

    private (int? Year, string Source) Pick()
    {
        if (Override is not null)
        {
            return (Override.Year, "manual override");
        }

        (int? Year, string Source) best =
            Entry.MusicBrainz?.Year is int mb ? (mb, "MusicBrainz")
            : Entry.IsLiveVersion && Entry.TitleYear is int titleYear ? (titleYear, "year in live title")
            : Entry.SpotifySearch?.Year is int sp ? (sp, "Spotify search")
            : (null, "none");

        // The playlist's copy can't predate the song, so its album date is an upper bound.
        if (ReleaseDates.ParseYear(Entry.AlbumReleaseDate) is int album && (best.Year is null || album < best.Year))
        {
            return (album, "album date");
        }

        return best;
    }
}
