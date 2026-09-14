using System;

namespace SpotifyByYear.Models;

/// <summary>
/// One entry in a playlist (usually a track; can be an episode or a local file),
/// plus the raw JSON Spotify returned for it.
/// </summary>
public sealed record PlaylistTrackInfo(
    int Position,
    string Type,
    string? TrackId,
    string? Isrc,
    string Name,
    string Artists,
    string PrimaryArtist,
    string Album,
    string? AlbumType,
    string? ReleaseDate,
    string? ReleaseDatePrecision,
    bool IsLocal,
    DateTimeOffset? AddedAt,
    string RawJson)
{
    /// <summary>Year of the album this copy is on. Not the song's original year for compilations/remasters.</summary>
    public int? AlbumYear => ReleaseDates.ParseYear(ReleaseDate);

    /// <summary>Only regular Spotify tracks can be looked up in MusicBrainz / Spotify search.</summary>
    public bool CanResolveYear => Type == "track" && !IsLocal && TrackId is not null;
}
