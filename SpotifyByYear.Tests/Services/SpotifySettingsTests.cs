using SpotifyByYear.Services;
using SpotifyByYear.Tests.TestSupport;

namespace SpotifyByYear.Tests.Services;

public sealed class SpotifySettingsTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Missing_file_explains_how_to_create_it()
    {
        var error = Assert.Throws<InvalidOperationException>(() => SpotifySettings.Load(_dir.Path));
        Assert.Contains("appsettings.Local.example.json", error.Message);
    }

    [Fact]
    public void Missing_Spotify_section_is_rejected()
    {
        Write("""{ "Other": {} }""");

        Assert.Throws<InvalidOperationException>(() => SpotifySettings.Load(_dir.Path));
    }

    [Theory]
    [InlineData("""{ "Spotify": { } }""")]
    [InlineData("""{ "Spotify": { "ClientId": "  " } }""")]
    [InlineData("""{ "Spotify": { "ClientId": "paste-your-spotify-client-id-here" } }""")]
    public void Missing_or_placeholder_client_id_is_rejected(string json)
    {
        Write(json);

        var error = Assert.Throws<InvalidOperationException>(() => SpotifySettings.Load(_dir.Path));
        Assert.Contains("ClientId", error.Message);
    }

    [Fact]
    public void Default_redirect_uri_is_used_when_not_set()
    {
        Write("""{ "Spotify": { "ClientId": " abc123 " } }""");

        var settings = SpotifySettings.Load(_dir.Path);

        Assert.Equal("abc123", settings.ClientId);
        Assert.Equal(new Uri("http://127.0.0.1:5543/callback"), settings.RedirectUri);
    }

    [Theory]
    [InlineData("http://example.com/callback")]
    [InlineData("https://127.0.0.1:5543/callback")]
    public void Redirect_uri_must_be_http_loopback(string redirectUri)
    {
        Write($$"""{ "Spotify": { "ClientId": "abc123", "RedirectUri": "{{redirectUri}}" } }""");

        var error = Assert.Throws<InvalidOperationException>(() => SpotifySettings.Load(_dir.Path));
        Assert.Contains("RedirectUri", error.Message);
    }

    [Fact]
    public void Custom_loopback_redirect_uri_is_accepted()
    {
        Write("""{ "Spotify": { "ClientId": "abc123", "RedirectUri": "http://127.0.0.1:6000/cb" } }""");

        Assert.Equal(new Uri("http://127.0.0.1:6000/cb"), SpotifySettings.Load(_dir.Path).RedirectUri);
    }

    private void Write(string json) => File.WriteAllText(_dir.File(SpotifySettings.FileName), json);
}
