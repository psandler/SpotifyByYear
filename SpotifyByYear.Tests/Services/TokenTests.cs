using SpotifyAPI.Web;
using SpotifyByYear.Services;
using SpotifyByYear.Tests.TestSupport;

namespace SpotifyByYear.Tests.Services;

public sealed class TokenStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Saved_token_is_loaded_back()
    {
        var store = new TokenStore(_dir.Path);
        var createdAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        store.Save(new PKCETokenResponse { AccessToken = "access", RefreshToken = "refresh", ExpiresIn = 3600, CreatedAt = createdAt, Scope = "a b" });

        var loaded = new TokenStore(_dir.Path).Load();

        Assert.NotNull(loaded);
        Assert.Equal("access", loaded.AccessToken);
        Assert.Equal("refresh", loaded.RefreshToken);
        Assert.Equal(3600, loaded.ExpiresIn);
        Assert.Equal(createdAt, loaded.CreatedAt.ToUniversalTime());
        Assert.Equal("a b", loaded.Scope);
    }

    [Fact]
    public void Missing_or_corrupt_file_loads_as_null()
    {
        var store = new TokenStore(_dir.Path);
        Assert.Null(store.Load());

        File.WriteAllText(_dir.File("token.json"), "{ nope");
        Assert.Null(store.Load());
    }

    [Fact]
    public void Clear_deletes_the_token()
    {
        var store = new TokenStore(_dir.Path);
        store.Save(new PKCETokenResponse { AccessToken = "access", RefreshToken = "refresh" });

        store.Clear();

        Assert.False(File.Exists(_dir.File("token.json")));
    }
}

/// <summary>Regression tests for the "String is empty or null (Parameter 'refreshToken')" bug.</summary>
public class SpotifyServiceTokenTests
{
    [Fact]
    public void Refresh_without_a_new_refresh_token_keeps_the_previous_one()
    {
        var refreshed = new PKCETokenResponse { AccessToken = "new-access", RefreshToken = null! };

        var keep = SpotifyService.KeepRefreshToken(refreshed, "previous-refresh");

        Assert.Equal("previous-refresh", keep);
        Assert.Equal("previous-refresh", refreshed.RefreshToken);
    }

    [Fact]
    public void Refresh_with_a_new_refresh_token_uses_it()
    {
        var refreshed = new PKCETokenResponse { AccessToken = "new-access", RefreshToken = "rotated-refresh" };

        var keep = SpotifyService.KeepRefreshToken(refreshed, "previous-refresh");

        Assert.Equal("rotated-refresh", keep);
        Assert.Equal("rotated-refresh", refreshed.RefreshToken);
    }

    [Fact]
    public void Expired_token_is_only_usable_with_a_refresh_token()
    {
        var longAgo = DateTime.UtcNow.AddDays(-1);

        Assert.False(SpotifyService.CanStillBeUsed(new PKCETokenResponse { RefreshToken = null!, CreatedAt = longAgo, ExpiresIn = 3600 }));
        Assert.True(SpotifyService.CanStillBeUsed(new PKCETokenResponse { RefreshToken = "refresh", CreatedAt = longAgo, ExpiresIn = 3600 }));
        Assert.True(SpotifyService.CanStillBeUsed(new PKCETokenResponse { RefreshToken = null!, CreatedAt = DateTime.UtcNow, ExpiresIn = 3600 }));
    }

    [Theory]
    [InlineData("playlist-read-private playlist-read-collaborative playlist-modify-private playlist-modify-public", true)]
    [InlineData("playlist-modify-public playlist-read-private playlist-modify-private playlist-read-collaborative user-read-email", true)]
    [InlineData("playlist-read-private playlist-read-collaborative", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Token_must_have_every_required_scope(string? scope, bool expected) =>
        Assert.Equal(expected, SpotifyService.HasRequiredScopes(new PKCETokenResponse { Scope = scope! }));
}

public class BrowserLauncherTests
{
    // Only rejected URLs are tested: a valid URL would really open the browser.
    [Theory]
    [InlineData("http://accounts.spotify.com/authorize")]
    [InlineData("https://evil.example.com/authorize")]
    [InlineData("https://accounts.spotify.com.evil.example.com/")]
    [InlineData("file:///C:/Windows/System32/notepad.exe")]
    public void Refuses_anything_but_https_Spotify_accounts_urls(string url) =>
        Assert.Throws<ArgumentException>(() => BrowserLauncher.Open(new Uri(url)));
}
