using System.Diagnostics;
using SpotifyByYear.Models;
using SpotifyByYear.Services;
using SpotifyByYear.Tests.TestSupport;
using SpotifyByYear.ViewModels;
using static SpotifyByYear.Tests.TestSupport.FakeMusicBrainzClient;
using static SpotifyByYear.Tests.TestSupport.TestTracks;

namespace SpotifyByYear.Tests.ViewModels;

public sealed class MainViewModelTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly FakeSpotifyService _spotify = new();
    private readonly FakeMusicBrainzClient _musicBrainz = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Loading_playlists_connects_and_sorts_by_name()
    {
        _spotify.Playlists.AddRange([new("3", "zebra mix", 12), new("1", "Road Trip", 80), new("2", "80s", 45)]);
        var vm = CreateViewModel();

        await vm.LoadPlaylistsCommand.ExecuteAsync(null);

        Assert.True(_spotify.IsConnected);
        Assert.Equal(["80s", "Road Trip", "zebra mix"], vm.Playlists.Select(p => p.Name));
        Assert.Equal("Signed in as Test User. You own 3 playlists.", vm.StatusText);
    }

    [Fact]
    public async Task Connection_failure_is_shown_in_the_status()
    {
        _spotify.ConnectException = new InvalidOperationException("Missing appsettings.Local.json.");
        var vm = CreateViewModel();

        await vm.LoadPlaylistsCommand.ExecuteAsync(null);

        Assert.Equal("Error: Missing appsettings.Local.json.", vm.StatusText);
        Assert.Empty(vm.Playlists);
        Assert.Equal("", vm.YearsStatus); // no lookup pass without playlists
    }

    [Fact]
    public async Task Select_all_checkbox_is_tri_state()
    {
        _spotify.Playlists.AddRange([new("1", "A", 1), new("2", "B", 1), new("3", "C", 1)]);
        var vm = CreateViewModel();
        await vm.LoadPlaylistsCommand.ExecuteAsync(null);

        Assert.False(vm.AllSelected);
        Assert.Equal("0 of 3 selected", vm.SelectionSummary);

        vm.Playlists[0].IsSelected = true;
        Assert.Null(vm.AllSelected);
        Assert.Equal("1 of 3 selected", vm.SelectionSummary);

        vm.AllSelected = true;
        Assert.True(vm.AllSelected);
        Assert.All(vm.Playlists, p => Assert.True(p.IsSelected));

        vm.Playlists[1].IsSelected = false;
        Assert.Null(vm.AllSelected);

        vm.AllSelected = false; // what the checkbox sends when clicked while indeterminate
        Assert.False(vm.AllSelected);
        Assert.Equal("0 of 3 selected", vm.SelectionSummary);
    }

    [Fact]
    public async Task Loading_playlists_starts_a_library_wide_year_lookup()
    {
        _spotify.Playlists.AddRange([new("p1", "Mix", 2), new("p2", "Party", 2)]);
        _spotify.Items["p1"] = [Track(position: 1), Track(name: "Shared Song", isrc: "SHARED-ISRC", position: 2)];
        _spotify.Items["p2"] =
        [
            Track(name: "Shared Song", isrc: "SHARED-ISRC", position: 1),              // same song, other playlist
            Track(name: "My Local File", isLocal: true, trackId: null, position: 2),   // never looked up
        ];
        _musicBrainz.ByIsrc["USRC19206280"] = [Recording("1976")];
        _musicBrainz.ByIsrc["SHARED-ISRC"] = [Recording("1980", title: "Shared Song")];
        var vm = CreateViewModel();

        await vm.LoadPlaylistsCommand.ExecuteAsync(null);
        await WaitUntil(() => vm.YearsStatus.Contains("songs done"));

        Assert.Equal("Release years: 2 songs done.", vm.YearsStatus);
        Assert.Equal(2, _spotify.SearchQueries.Count);                 // one lookup per unique song
        Assert.Equal(["isrc:USRC19206280", "isrc:SHARED-ISRC"], _musicBrainz.Calls);
        Assert.True(File.Exists(_dir.File("release-years.json")));
    }

    [Fact]
    public async Task Cached_years_show_immediately_when_a_playlist_is_opened()
    {
        _spotify.Playlists.Add(new PlaylistSummary("p1", "Mix", 2));
        _spotify.Items["p1"] =
        [
            Track(position: 1),
            Track(name: "My Local File", isLocal: true, trackId: null, releaseDate: null, position: 2),
        ];
        _musicBrainz.ByIsrc["USRC19206280"] = [Recording("1976")];
        var vm = CreateViewModel();
        await vm.LoadPlaylistsCommand.ExecuteAsync(null);
        await WaitUntil(() => vm.YearsStatus.Contains("songs done"));
        var lookupsSoFar = _musicBrainz.Calls.Count;

        vm.SelectedPlaylist = vm.Playlists[0];
        await WaitUntil(() => vm.Tracks.Count == 2);

        var richGirl = vm.Tracks[0];
        Assert.Equal(1976, richGirl.Resolution?.Year);
        Assert.Equal("Daryl Hall & John Oates · 1976 (MusicBrainz)", richGirl.Subtitle);
        Assert.Contains("Original release year: 1976 (from MusicBrainz)", richGirl.ResolvedText);
        Assert.Contains("MusicBrainz: 1976 via isrc", richGirl.MusicBrainzText);
        Assert.Contains("album type: compilation", richGirl.AlbumDateText);
        Assert.Contains("local file", vm.Tracks[1].Subtitle);
        Assert.Equal("Mix: 2 items", vm.TracksStatus);
        Assert.Equal(lookupsSoFar, _musicBrainz.Calls.Count); // opening a playlist looks nothing up again
    }

    [Fact]
    public async Task Clicking_an_unresolved_track_looks_it_up_straight_away()
    {
        _spotify.Playlists.Add(new PlaylistSummary("p1", "Mix", 1));
        _spotify.Items["p1"] = [Track()];
        var vm = CreateViewModel();
        await vm.LoadPlaylistsCommand.ExecuteAsync(null);      // pass runs with no MusicBrainz match
        await WaitUntil(() => vm.YearsStatus.Contains("songs done"));
        vm.SelectedPlaylist = vm.Playlists[0];
        await WaitUntil(() => vm.Tracks.Count == 1);

        _musicBrainz.ByIsrc["USRC19206280"] = [Recording("1976")];
        await vm.LookUpSelectedTrackAgainCommand.ExecuteAsync(null); // nothing selected yet: no-op
        vm.SelectedTrack = vm.Tracks[0];
        await vm.LookUpSelectedTrackAgainCommand.ExecuteAsync(null);

        Assert.Equal(1976, vm.Tracks[0].Resolution?.Year);
    }

    [Fact]
    public async Task Selecting_no_playlist_clears_the_tracks()
    {
        _spotify.Playlists.Add(new PlaylistSummary("p1", "Mix", 1));
        _spotify.Items["p1"] = [Track()];
        var vm = CreateViewModel();
        await vm.LoadPlaylistsCommand.ExecuteAsync(null);
        vm.SelectedPlaylist = vm.Playlists[0];
        await WaitUntil(() => vm.Tracks.Count == 1);

        vm.SelectedPlaylist = null;
        await WaitUntil(() => vm.Tracks.Count == 0);

        Assert.Equal("Select a playlist to see its tracks.", vm.TracksStatus);
    }

    [Fact]
    public async Task Year_lookup_without_playlists_says_so()
    {
        var vm = CreateViewModel();

        await vm.ResolveAllYearsCommand.ExecuteAsync(null);

        Assert.Equal("Load playlists first.", vm.YearsStatus);
    }

    private MainViewModel CreateViewModel() =>
        new(_spotify, new ReleaseYearResolver(new ReleaseYearCache(_dir.Path), _musicBrainz, _spotify));

    private static async Task WaitUntil(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(10))
            {
                Assert.Fail("Timed out waiting for the view model.");
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
