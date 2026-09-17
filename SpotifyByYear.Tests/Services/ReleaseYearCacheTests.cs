using SpotifyByYear.Models;
using SpotifyByYear.Services;
using SpotifyByYear.Tests.TestSupport;

namespace SpotifyByYear.Tests.Services;

public sealed class ReleaseYearCacheTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Starts_empty_and_creates_an_empty_overrides_file()
    {
        var cache = new ReleaseYearCache(_dir.Path);

        Assert.Null(cache.Get("t1"));
        Assert.True(File.Exists(_dir.File("year-overrides.json")));
        Assert.Null(cache.GetOverride("t1"));
    }

    [Fact]
    public void Saved_entries_are_read_back_by_a_new_instance()
    {
        var cache = new ReleaseYearCache(_dir.Path);
        cache.Set(new ReleaseYearEntry
        {
            TrackId = "t1",
            Isrc = "USRC19206280",
            Title = "Rich Girl",
            AlbumReleaseDate = "2010-03-01",
            LogicVersion = 7,
            IsLiveVersion = true,
            TitleYear = 2007,
            MusicBrainz = new SourceResult { Year = 1976, Method = "isrc", Candidates = ["1976 · Rich Girl"] },
            SpotifySearch = new SourceResult { Error = "boom" },
        });
        cache.Save();

        var reloaded = new ReleaseYearCache(_dir.Path).Get("t1");

        Assert.NotNull(reloaded);
        Assert.Equal("Rich Girl", reloaded.Title);
        Assert.Equal(7, reloaded.LogicVersion);
        Assert.True(reloaded.IsLiveVersion);
        Assert.Equal(2007, reloaded.TitleYear);
        Assert.Equal(1976, reloaded.MusicBrainz?.Year);
        Assert.Equal(["1976 · Rich Girl"], reloaded.MusicBrainz?.Candidates);
        Assert.Equal("boom", reloaded.SpotifySearch?.Error);
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
    }

    [Fact]
    public void Save_without_changes_writes_nothing()
    {
        var cache = new ReleaseYearCache(_dir.Path);
        cache.Get("t1");

        cache.Save();

        Assert.False(File.Exists(_dir.File("release-years.json")));
    }

    [Fact]
    public void Overrides_are_read_from_their_own_file()
    {
        File.WriteAllText(_dir.File("year-overrides.json"),
            """{ "overrides": { "t1": { "year": 1977, "note": "single release" } } }""");

        var yearOverride = new ReleaseYearCache(_dir.Path).GetOverride("t1");

        Assert.Equal(1977, yearOverride?.Year);
        Assert.Equal("single release", yearOverride?.Note);
    }

    [Fact]
    public void Invalid_overrides_file_is_reported_not_ignored()
    {
        File.WriteAllText(_dir.File("year-overrides.json"), "{ not json");

        var error = Assert.Throws<InvalidOperationException>(() => new ReleaseYearCache(_dir.Path).Get("t1"));
        Assert.Contains("year-overrides.json", error.Message);
    }

    [Fact]
    public void Unreadable_cache_is_set_aside_instead_of_overwritten()
    {
        File.WriteAllText(_dir.File("release-years.json"), "garbage");

        Assert.Null(new ReleaseYearCache(_dir.Path).Get("t1"));
        Assert.Single(Directory.GetFiles(_dir.Path, "release-years.json.unreadable-*"));
    }

    [Fact]
    public void Cache_with_unknown_schema_version_starts_empty()
    {
        File.WriteAllText(_dir.File("release-years.json"), """{ "schemaVersion": 99, "entries": { "t1": { "trackId": "t1" } } }""");

        Assert.Null(new ReleaseYearCache(_dir.Path).Get("t1"));
    }
}
