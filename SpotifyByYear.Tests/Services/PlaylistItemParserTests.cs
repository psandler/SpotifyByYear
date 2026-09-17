using SpotifyByYear.Services;

namespace SpotifyByYear.Tests.Services;

public class PlaylistItemParserTests
{
    // Synthetic page in the Feb 2026 shape ("item"), plus the legacy "track" key and edge cases.
    private const string Page = """
        {
          "items": [
            {
              "added_at": "2023-11-08T20:27:53Z",
              "is_local": false,
              "primary_color": null,
              "item": {
                "type": "track", "id": "t1", "name": "Rich Girl",
                "external_ids": { "isrc": "USRC19206280" },
                "artists": [ { "name": "Daryl Hall & John Oates" }, { "name": "Guest Artist" } ],
                "album": { "name": "70s 100 Hits", "album_type": "compilation", "release_date": "2010-03-01", "release_date_precision": "day" }
              }
            },
            {
              "is_local": false,
              "track": {
                "type": "track", "id": "t2", "name": "Legacy Shape",
                "artists": [ { "name": "Someone" } ],
                "album": { "name": "An Album", "album_type": "album", "release_date": "1985", "release_date_precision": "year" }
              }
            },
            {
              "is_local": false,
              "item": {
                "type": "episode", "id": "e1", "name": "Episode 1",
                "release_date": "2020-01-02", "release_date_precision": "day",
                "show": { "name": "The Show", "publisher": "The Publisher" }
              }
            },
            {
              "is_local": true,
              "item": { "type": "track", "id": null, "name": "My Local File", "artists": [], "album": { "name": "", "release_date": null } }
            },
            { "is_local": false, "item": null }
          ]
        }
        """;

    [Fact]
    public void Positions_continue_from_the_previous_page()
    {
        var items = PlaylistItemParser.ParsePage(Page, firstPosition: 100);

        Assert.Equal([101, 102, 103, 104, 105], items.Select(i => i.Position));
    }

    [Fact]
    public void Track_fields_are_parsed()
    {
        var track = PlaylistItemParser.ParsePage(Page, 0)[0];

        Assert.Equal("track", track.Type);
        Assert.Equal("t1", track.TrackId);
        Assert.Equal("USRC19206280", track.Isrc);
        Assert.Equal("Rich Girl", track.Name);
        Assert.Equal("Daryl Hall & John Oates, Guest Artist", track.Artists);
        Assert.Equal("Daryl Hall & John Oates", track.PrimaryArtist);
        Assert.Equal("70s 100 Hits", track.Album);
        Assert.Equal("compilation", track.AlbumType);
        Assert.Equal("2010-03-01", track.ReleaseDate);
        Assert.Equal("day", track.ReleaseDatePrecision);
        Assert.Equal(2010, track.AlbumYear);
        Assert.Equal(new DateTimeOffset(2023, 11, 8, 20, 27, 53, TimeSpan.Zero), track.AddedAt);
        Assert.True(track.CanResolveYear);
    }

    [Fact]
    public void Legacy_track_key_is_still_understood()
    {
        var track = PlaylistItemParser.ParsePage(Page, 0)[1];

        Assert.Equal("Legacy Shape", track.Name);
        Assert.Equal(1985, track.AlbumYear);
    }

    [Fact]
    public void Episodes_use_show_details_and_cannot_be_resolved()
    {
        var episode = PlaylistItemParser.ParsePage(Page, 0)[2];

        Assert.Equal("episode", episode.Type);
        Assert.Equal("The Publisher", episode.PrimaryArtist);
        Assert.Equal("The Show", episode.Album);
        Assert.Equal("2020-01-02", episode.ReleaseDate);
        Assert.False(episode.CanResolveYear);
    }

    [Fact]
    public void Local_files_cannot_be_resolved()
    {
        var local = PlaylistItemParser.ParsePage(Page, 0)[3];

        Assert.True(local.IsLocal);
        Assert.Null(local.TrackId);
        Assert.Null(local.ReleaseDate);
        Assert.False(local.CanResolveYear);
    }

    [Fact]
    public void Missing_item_is_marked_unavailable()
    {
        var missing = PlaylistItemParser.ParsePage(Page, 0)[4];

        Assert.Equal("(unavailable)", missing.Name);
        Assert.Equal("unknown", missing.Type);
        Assert.False(missing.CanResolveYear);
    }

    [Fact]
    public void Raw_json_keeps_every_field_readably()
    {
        var track = PlaylistItemParser.ParsePage(Page, 0)[0];

        Assert.Contains("\"primary_color\"", track.RawJson);        // field the library doesn't model
        Assert.Contains("Daryl Hall & John Oates", track.RawJson);  // not escaped as &
        Assert.Contains(Environment.NewLine, track.RawJson);        // indented
    }

    [Fact]
    public void Page_without_items_is_empty() =>
        Assert.Empty(PlaylistItemParser.ParsePage("{}", 0));
}
