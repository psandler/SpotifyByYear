using SpotifyByYear.Models;

namespace SpotifyByYear.Tests.TestSupport;

public static class TestTracks
{
    /// <summary>A playlist entry; defaults describe "Rich Girl" on the 2010 compilation "70s 100 Hits".</summary>
    public static PlaylistTrackInfo Track(
        string name = "Rich Girl",
        string artist = "Daryl Hall & John Oates",
        string? isrc = "USRC19206280",
        string? releaseDate = "2010-03-01",
        string? albumType = "compilation",
        string type = "track",
        bool isLocal = false,
        string? trackId = "",
        int position = 1) =>
        new(
            Position: position,
            Type: type,
            TrackId: trackId == "" ? "id:" + name : trackId,
            Isrc: isrc,
            Name: name,
            Artists: artist,
            PrimaryArtist: artist,
            Album: "Test Album",
            AlbumType: albumType,
            ReleaseDate: releaseDate,
            ReleaseDatePrecision: "day",
            IsLocal: isLocal,
            AddedAt: null,
            RawJson: "{}");
}
