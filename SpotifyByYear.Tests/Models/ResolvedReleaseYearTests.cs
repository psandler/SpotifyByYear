using SpotifyByYear.Models;

namespace SpotifyByYear.Tests.Models;

public class ResolvedReleaseYearTests
{
    [Fact]
    public void Manual_override_wins_over_everything()
    {
        var resolved = Resolve(Entry(musicBrainz: 1976, albumDate: "1970-01-01"), new YearOverride { Year = 1977 });

        Assert.Equal(1977, resolved.Year);
        Assert.Equal("manual override", resolved.Source);
    }

    [Fact]
    public void MusicBrainz_is_preferred_over_Spotify_search()
    {
        var resolved = Resolve(Entry(musicBrainz: 1976, spotify: 1978, albumDate: "2010-03-01"));

        Assert.Equal(1976, resolved.Year);
        Assert.Equal("MusicBrainz", resolved.Source);
    }

    [Fact]
    public void Spotify_search_is_used_when_MusicBrainz_has_no_match()
    {
        var resolved = Resolve(Entry(spotify: 1973, albumDate: "1985"));

        Assert.Equal(1973, resolved.Year);
        Assert.Equal("Spotify search", resolved.Source);
    }

    [Fact]
    public void Album_year_is_an_upper_bound()
    {
        var resolved = Resolve(Entry(musicBrainz: 2007, spotify: 2007, albumDate: "2006-05-01"));

        Assert.Equal(2006, resolved.Year);
        Assert.Equal("album date", resolved.Source);
    }

    [Fact]
    public void Album_year_is_used_when_nothing_else_matched()
    {
        var resolved = Resolve(Entry(albumDate: "1999"));

        Assert.Equal(1999, resolved.Year);
        Assert.Equal("album date", resolved.Source);
    }

    [Fact]
    public void No_data_gives_no_year()
    {
        var resolved = Resolve(Entry());

        Assert.Null(resolved.Year);
        Assert.Equal("none", resolved.Source);
    }

    [Fact]
    public void Live_title_year_is_used_before_Spotify_search()
    {
        var resolved = Resolve(Entry(spotify: 2012, albumDate: "2020", isLive: true, titleYear: 2007));

        Assert.Equal(2007, resolved.Year);
        Assert.Equal("year in live title", resolved.Source);
    }

    [Fact]
    public void Title_year_is_ignored_for_non_live_tracks()
    {
        var resolved = Resolve(Entry(spotify: 2012, albumDate: "2020", isLive: false, titleYear: 2007));

        Assert.Equal(2012, resolved.Year);
    }

    [Fact]
    public void Sources_disagree_only_when_both_have_different_years()
    {
        Assert.True(Resolve(Entry(musicBrainz: 1976, spotify: 1978)).SourcesDisagree);
        Assert.False(Resolve(Entry(musicBrainz: 1976, spotify: 1976)).SourcesDisagree);
        Assert.False(Resolve(Entry(musicBrainz: 1976)).SourcesDisagree);
    }

    private static ResolvedReleaseYear Resolve(ReleaseYearEntry entry, YearOverride? yearOverride = null) =>
        new(entry, yearOverride);

    private static ReleaseYearEntry Entry(
        int? musicBrainz = null,
        int? spotify = null,
        string? albumDate = null,
        bool isLive = false,
        int? titleYear = null) =>
        new()
        {
            TrackId = "t",
            AlbumReleaseDate = albumDate,
            IsLiveVersion = isLive,
            TitleYear = titleYear,
            MusicBrainz = new SourceResult { Year = musicBrainz },
            SpotifySearch = new SourceResult { Year = spotify },
        };
}
