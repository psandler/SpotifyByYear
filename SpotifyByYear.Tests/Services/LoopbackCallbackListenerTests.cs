using System.Net;
using System.Net.Sockets;
using SpotifyByYear.Services;

namespace SpotifyByYear.Tests.Services;

/// <summary>Each test listens on its own free port on 127.0.0.1. No browser is involved.</summary>
public class LoopbackCallbackListenerTests
{
    private static readonly HttpClient Http = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_code_when_the_state_matches()
    {
        var redirect = FreeRedirectUri();
        using var listener = LoopbackCallbackListener.Start(redirect);
        var wait = listener.WaitForCodeAsync("STATE", Ct);

        var response = await Http.GetAsync(new Uri(redirect, "?code=the-code&state=STATE"), Ct);

        Assert.Equal("the-code", await wait);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Signed in to SpotifyByYear", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Ignores_other_paths_and_wrong_state_then_accepts_the_real_callback()
    {
        var redirect = FreeRedirectUri();
        using var listener = LoopbackCallbackListener.Start(redirect);
        var wait = listener.WaitForCodeAsync("STATE", Ct);

        var favicon = await Http.GetAsync(new Uri(redirect, "/favicon.ico"), Ct);
        var wrongState = await Http.GetAsync(new Uri(redirect, "?code=forged&state=WRONG"), Ct);
        await Http.GetAsync(new Uri(redirect, "?code=real&state=STATE"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, favicon.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongState.StatusCode);
        Assert.Equal("real", await wait);
    }

    [Fact]
    public async Task Error_from_Spotify_is_thrown()
    {
        var redirect = FreeRedirectUri();
        using var listener = LoopbackCallbackListener.Start(redirect);
        var wait = listener.WaitForCodeAsync("STATE", Ct);

        await Http.GetAsync(new Uri(redirect, "?error=access_denied&state=STATE"), Ct);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => wait);
        Assert.Contains("access_denied", error.Message);
    }

    [Fact]
    public async Task Idle_connections_do_not_block_the_real_callback()
    {
        var redirect = FreeRedirectUri();
        using var listener = LoopbackCallbackListener.Start(redirect);
        var wait = listener.WaitForCodeAsync("STATE", Ct);

        using var idle = new TcpClient();
        await idle.ConnectAsync(IPAddress.Loopback, redirect.Port, Ct); // like a browser's speculative connection
        await Http.GetAsync(new Uri(redirect, "?code=the-code&state=STATE"), Ct);

        Assert.Equal("the-code", await wait);
    }

    [Fact]
    public async Task Waiting_can_be_cancelled()
    {
        using var listener = LoopbackCallbackListener.Start(FreeRedirectUri());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listener.WaitForCodeAsync("STATE", cts.Token));
    }

    [Fact]
    public void Port_already_in_use_gives_a_clear_error()
    {
        var redirect = FreeRedirectUri();
        using var first = LoopbackCallbackListener.Start(redirect);

        var error = Assert.Throws<InvalidOperationException>(() => LoopbackCallbackListener.Start(redirect));
        Assert.Contains($"port {redirect.Port}", error.Message);
    }

    private static Uri FreeRedirectUri()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return new Uri($"http://127.0.0.1:{port}/callback");
    }
}
