using SpotifyByYear.Models;
using SpotifyByYear.Services;

namespace SpotifyByYear.Tests.Live;

/// <summary>
/// Tests against the real MusicBrainz and Spotify services. Skipped unless SPOTIFYBYYEAR_LIVE_TESTS=1.
/// They never open a browser: Spotify tests only run with a saved login that can be refreshed.
/// </summary>
public class LiveApiTests
{
    private const string EnableVariable = "SPOTIFYBYYEAR_LIVE_TESTS";

    private static bool Enabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MusicBrainz_finds_Rich_Girl_by_isrc()
    {
        Assert.SkipUnless(Enabled, $"Set {EnableVariable}=1 to run tests against real services.");
        using var client = new MusicBrainzClient();

        var recordings = await client.LookupByIsrcAsync("USRC19206280", Ct);

        Assert.Contains(recordings, r => r.Title == "Rich Girl" && ReleaseDates.ParseYear(r.FirstReleaseDate) == 1976);
    }

    [Fact]
    public async Task Spotify_lists_owned_playlists_with_the_saved_login()
    {
        Assert.SkipUnless(Enabled, $"Set {EnableVariable}=1 to run tests against real services.");
        var spotify = new SpotifyService(new TokenStore());

        var connected = await spotify.TryConnectWithSavedLoginAsync(Ct);
        Assert.SkipUnless(connected, "No usable saved Spotify login. Sign in with the app first.");

        var playlists = await spotify.GetOwnedPlaylistsAsync(Ct);

        Assert.NotEmpty(playlists);
    }
}
