using System.Collections.Generic;

namespace SpotifyByYear.Models;

/// <summary>A track returned by Spotify search, reduced to what year matching needs.</summary>
public sealed record SpotifySearchHit(
    string? TrackId,
    string Name,
    IReadOnlyList<string> Artists,
    string? AlbumId,
    string AlbumName,
    string? AlbumType,
    string? ReleaseDate);
