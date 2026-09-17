using SpotifyByYear.Models;
using SpotifyByYear.Services;
using SpotifyByYear.Tests.TestSupport;
using static SpotifyByYear.Tests.TestSupport.FakeMusicBrainzClient;
using static SpotifyByYear.Tests.TestSupport.FakeSpotifyService;
using static SpotifyByYear.Tests.TestSupport.TestTracks;

namespace SpotifyByYear.Tests.Services;

public sealed class ReleaseYearResolverTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly FakeMusicBrainzClient _musicBrainz = new();
    private readonly FakeSpotifyService _spotify = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Compilation_copy_gets_the_original_year_from_the_isrc()
    {
        _musicBrainz.ByIsrc["USRC19206280"] = [Recording("1976")];
        _spotify.SearchHits = [Hit("2010-03-01", albumType: "compilation"), Hit("1976-01-01")];

        var resolved = await CreateResolver().ResolveAsync(Track(), force: false, Ct);

        Assert.Equal(1976, resolved.Year);
        Assert.Equal("MusicBrainz", resolved.Source);
        Assert.Equal("isrc", resolved.Entry.MusicBrainz?.Method);
        Assert.Equal(1976, resolved.Entry.SpotifySearch?.Year);
        Assert.Equal(["isrc:USRC19206280"], _musicBrainz.Calls); // plain title: no MusicBrainz search needed
    }

    [Fact]
    public async Task Isrc_match_with_a_different_title_is_ignored()
    {
        _musicBrainz.ByIsrc["USRC19206280"] = [Recording("1990", title: "Some Other Song")];
        _musicBrainz.SearchResults = [Recording("1976")];

        var resolved = await CreateResolver().ResolveAsync(Track(), force: false, Ct);

        Assert.Equal(1976, resolved.Year);
        Assert.Equal("search", resolved.Entry.MusicBrainz?.Method);
    }

    [Fact]
    public async Task Remaster_title_also_searches_and_the_earliest_recording_wins()
    {
        _musicBrainz.ByIsrc["USRC19206280"] = [Recording("2003")];
        _musicBrainz.SearchResults = [Recording("1986"), Recording("1976")];
        var track = Track(name: "Rich Girl - 2003 Remaster", releaseDate: "2003-01-01", albumType: "album");

        var resolved = await CreateResolver().ResolveAsync(track, force: false, Ct);

        Assert.Equal(1976, resolved.Year);
        Assert.Equal("search", resolved.Entry.MusicBrainz?.Method);
        Assert.Contains("search:Rich Girl|Daryl Hall & John Oates", _musicBrainz.Calls); // searched with the clean title
    }

    [Fact]
    public async Task Search_ignores_live_versions_low_scores_and_other_artists()
    {
        _musicBrainz.SearchResults =
        [
            Recording("1970", disambiguation: "live, 1970: Somewhere"),
            Recording("1971", score: 50),
            Recording("1972", artist: "A Cover Band"),
            Recording("1976"),
        ];

        var resolved = await CreateResolver().ResolveAsync(Track(isrc: null), force: false, Ct);

        Assert.Equal(1976, resolved.Year);
    }

    [Fact]
    public async Task Live_version_accepts_the_live_isrc_recording_without_searching()
    {
        _musicBrainz.ByIsrc["LIVE-ISRC"] =
            [Recording("2007-10-01", title: "Valerie", artist: "Amy Winehouse", disambiguation: "live, BBC Live Lounge")];
        var track = LiveValerie(isrc: "LIVE-ISRC");

        var resolved = await CreateResolver().ResolveAsync(track, force: false, Ct);

        Assert.Equal(2007, resolved.Year);
        Assert.Equal("isrc", resolved.Entry.MusicBrainz?.Method);
        Assert.DoesNotContain(_musicBrainz.Calls, c => c.StartsWith("search:", StringComparison.Ordinal));
        Assert.Equal($"{track.Name}|Amy Winehouse", Assert.Single(_spotify.SearchQueries)); // Spotify searched with the full live title
    }

    [Fact]
    public async Task Live_version_search_only_accepts_a_live_recording_from_the_title_year()
    {
        _musicBrainz.SearchResults =
        [
            Recording("2006", title: "Valerie", artist: "Amy Winehouse"),                                  // studio
            Recording("2008", title: "Valerie", artist: "Amy Winehouse", disambiguation: "live, 2008 Glastonbury"),
            Recording("2007-12-01", title: "Valerie", artist: "Amy Winehouse", disambiguation: "live, 2007 BBC"),
        ];

        var resolved = await CreateResolver().ResolveAsync(LiveValerie(isrc: null), force: false, Ct);

        Assert.Equal(2007, resolved.Year);
        Assert.Equal("search", resolved.Entry.MusicBrainz?.Method);
    }

    [Fact]
    public async Task Live_version_without_a_MusicBrainz_match_uses_the_year_in_the_title()
    {
        _musicBrainz.SearchResults = [Recording("2006", title: "Valerie", artist: "Amy Winehouse")]; // studio only

        var resolved = await CreateResolver().ResolveAsync(LiveValerie(isrc: null), force: false, Ct);

        Assert.Null(resolved.Entry.MusicBrainz?.Year);
        Assert.Equal(2007, resolved.Year);
        Assert.Equal("year in live title", resolved.Source);
    }

    [Fact]
    public async Task Spotify_search_skips_compilations_and_other_songs_and_picks_the_earliest_album()
    {
        _spotify.SearchHits =
        [
            Hit("2010-03-01", albumType: "compilation"),
            Hit("1978-05-16"),
            Hit("1976-08-01"),
            Hit("1960", name: "Some Other Song"),
            Hit("1961", artist: "Someone Else"),
        ];

        var resolved = await CreateResolver().ResolveAsync(Track(isrc: null), force: false, Ct);

        Assert.Equal(1976, resolved.Entry.SpotifySearch?.Year);
        Assert.Equal(1976, resolved.Year);
        Assert.Equal("Spotify search", resolved.Source);
    }

    [Fact]
    public async Task Failed_lookups_are_recorded_and_retried()
    {
        var resolver = CreateResolver();
        _musicBrainz.ThrowOnNextCall = new HttpRequestException("503 Service Unavailable");

        var first = await resolver.ResolveAsync(Track(), force: false, Ct);

        Assert.Contains("503", first.Entry.MusicBrainz?.Error);
        Assert.Null(resolver.TryGetCached(Track()));

        _musicBrainz.ByIsrc["USRC19206280"] = [Recording("1976")];
        var second = await resolver.ResolveAsync(Track(), force: false, Ct);

        Assert.Null(second.Entry.MusicBrainz?.Error);
        Assert.Equal(1976, second.Year);
    }

    [Fact]
    public async Task Cached_results_survive_a_restart_and_are_not_looked_up_again()
    {
        _musicBrainz.ByIsrc["USRC19206280"] = [Recording("1976")];
        var first = CreateResolver();
        await first.ResolveAsync(Track(), force: false, Ct);
        first.Flush();
        var callsAfterFirst = _musicBrainz.Calls.Count;

        var restarted = CreateResolver();
        var cached = restarted.TryGetCached(Track());
        var resolved = await restarted.ResolveAsync(Track(), force: false, Ct);

        Assert.Equal(1976, cached?.Year);
        Assert.Equal(1976, resolved.Year);
        Assert.Equal(callsAfterFirst, _musicBrainz.Calls.Count);
        Assert.Single(_spotify.SearchQueries);
    }

    [Fact]
    public async Task Entries_from_older_matching_logic_are_looked_up_again()
    {
        SeedCache(new ReleaseYearEntry
        {
            TrackId = Track().TrackId!,
            LogicVersion = ReleaseYearResolver.LogicVersion - 1,
            MusicBrainz = new SourceResult { Year = 1999, CheckedAt = DateTimeOffset.UtcNow },
            SpotifySearch = new SourceResult { Year = 1999, CheckedAt = DateTimeOffset.UtcNow },
        });
        _musicBrainz.ByIsrc["USRC19206280"] = [Recording("1976")];
        var resolver = CreateResolver();

        Assert.Null(resolver.TryGetCached(Track()));
        var resolved = await resolver.ResolveAsync(Track(), force: false, Ct);

        Assert.Equal(1976, resolved.Year);
    }

    [Fact]
    public async Task No_match_is_retried_after_30_days_but_not_before()
    {
        var trackId = Track().TrackId!;
        SeedCache(new ReleaseYearEntry
        {
            TrackId = trackId,
            LogicVersion = ReleaseYearResolver.LogicVersion,
            MusicBrainz = new SourceResult { Year = null, CheckedAt = DateTimeOffset.UtcNow.AddDays(-31) },
            SpotifySearch = new SourceResult { Year = null, CheckedAt = DateTimeOffset.UtcNow.AddDays(-1) },
        });
        var resolver = CreateResolver();

        Assert.Null(resolver.TryGetCached(Track()));
        await resolver.ResolveAsync(Track(), force: false, Ct);

        Assert.NotEmpty(_musicBrainz.Calls);     // stale "no match" retried
        Assert.Empty(_spotify.SearchQueries);    // recent "no match" kept
    }

    [Fact]
    public async Task Force_looks_up_again_even_when_cached()
    {
        var resolver = CreateResolver();
        await resolver.ResolveAsync(Track(), force: false, Ct);
        var calls = _musicBrainz.Calls.Count;

        await resolver.ResolveAsync(Track(), force: true, Ct);

        Assert.True(_musicBrainz.Calls.Count > calls);
        Assert.Equal(2, _spotify.SearchQueries.Count);
    }

    [Fact]
    public async Task Local_files_are_not_looked_up()
    {
        var local = Track(name: "My Local File", isLocal: true, trackId: null, releaseDate: "1999");

        var resolved = await CreateResolver().ResolveAsync(local, force: false, Ct);

        Assert.Empty(_musicBrainz.Calls);
        Assert.Empty(_spotify.SearchQueries);
        Assert.Equal(1999, resolved.Year);
    }

    [Fact]
    public async Task Manual_override_is_applied()
    {
        File.WriteAllText(_dir.File("year-overrides.json"),
            $$"""{ "overrides": { "{{Track().TrackId}}": { "year": 1977, "note": "single" } } }""");
        _musicBrainz.ByIsrc["USRC19206280"] = [Recording("1976")];

        var resolved = await CreateResolver().ResolveAsync(Track(), force: false, Ct);

        Assert.Equal(1977, resolved.Year);
        Assert.Equal("manual override", resolved.Source);
        Assert.Equal(1976, resolved.Entry.MusicBrainz?.Year); // lookups still stored for comparison
    }

    [Fact]
    public async Task Flush_writes_the_cache_file()
    {
        var resolver = CreateResolver();
        await resolver.ResolveAsync(Track(), force: false, Ct);

        resolver.Flush();

        Assert.True(File.Exists(_dir.File("release-years.json")));
    }

    private ReleaseYearResolver CreateResolver() =>
        new(new ReleaseYearCache(_dir.Path), _musicBrainz, _spotify);

    private void SeedCache(ReleaseYearEntry entry)
    {
        var cache = new ReleaseYearCache(_dir.Path);
        cache.Set(entry);
        cache.Save();
    }

    private static PlaylistTrackInfo LiveValerie(string? isrc) =>
        Track(
            name: "Valerie - Live At BBC Radio 1 Live Lounge, London / 2007",
            artist: "Amy Winehouse",
            isrc: isrc,
            releaseDate: "2009-01-01",
            albumType: "album");
}
