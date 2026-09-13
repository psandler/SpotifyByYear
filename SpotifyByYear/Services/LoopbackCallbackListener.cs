using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SpotifyByYear.Services;

/// <summary>
/// Minimal HTTP listener on 127.0.0.1 that catches the OAuth redirect from Spotify.
/// Uses a raw <see cref="TcpListener"/> rather than HttpListener, which needs a URL ACL
/// (admin rights) to bind to 127.0.0.1 on Windows.
/// </summary>
internal sealed class LoopbackCallbackListener : IDisposable
{
    // Browsers sometimes open speculative connections that never send a request.
    private static readonly TimeSpan PerConnectionTimeout = TimeSpan.FromSeconds(5);

    private readonly Uri _redirectUri;
    private readonly TcpListener _listener;

    private LoopbackCallbackListener(Uri redirectUri)
    {
        _redirectUri = redirectUri;
        _listener = new TcpListener(IPAddress.Loopback, redirectUri.Port);
        _listener.Start();
    }

    /// <summary>Starts listening immediately, so the browser can be opened afterwards.</summary>
    public static LoopbackCallbackListener Start(Uri redirectUri)
    {
        try
        {
            return new LoopbackCallbackListener(redirectUri);
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Could not listen on {redirectUri.GetLeftPart(UriPartial.Authority)} for the Spotify sign-in callback " +
                $"(is another app using port {redirectUri.Port}?): {ex.Message}", ex);
        }
    }

    /// <summary>Waits for Spotify to redirect back with an authorization code.</summary>
    public async Task<string> WaitForCodeAsync(string expectedState, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();

            string? target;
            using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                readTimeout.CancelAfter(PerConnectionTimeout);
                try
                {
                    target = await ReadRequestTargetAsync(stream, readTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    continue; // idle connection; wait for the next one
                }
            }

            var path = target?.Split('?', 2)[0];
            if (path != _redirectUri.AbsolutePath)
            {
                await WriteResponseAsync(stream, "404 Not Found", "Not found", "", cancellationToken);
                continue;
            }

            var query = HttpUtility.ParseQueryString(new Uri(_redirectUri, target).Query);

            if (query["state"] != expectedState)
            {
                // Stale or forged callback; ignore it and keep waiting for the real one.
                await WriteResponseAsync(stream, "400 Bad Request", "Sign-in failed",
                    "This sign-in link is out of date. Return to SpotifyByYear and try again.", cancellationToken);
                continue;
            }

            if (query["error"] is { } error)
            {
                await WriteResponseAsync(stream, "200 OK", "Sign-in cancelled",
                    "You can close this tab and return to SpotifyByYear.", cancellationToken);
                throw new InvalidOperationException($"Spotify sign-in did not complete: {error}");
            }

            if (query["code"] is not { Length: > 0 } code)
            {
                await WriteResponseAsync(stream, "400 Bad Request", "Sign-in failed",
                    "Spotify did not return an authorization code.", cancellationToken);
                continue;
            }

            await WriteResponseAsync(stream, "200 OK", "Signed in to SpotifyByYear",
                "You can close this tab and return to the app.", cancellationToken);
            return code;
        }
    }

    public void Dispose() => _listener.Stop();

    /// <summary>Reads the request line ("GET /callback?... HTTP/1.1") and skips the headers.</summary>
    private static async Task<string?> ReadRequestTargetAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (requestLine is null)
        {
            return null;
        }

        string? header;
        do
        {
            header = await reader.ReadLineAsync(cancellationToken);
        } while (!string.IsNullOrEmpty(header));

        var parts = requestLine.Split(' ');
        return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
    }

    private static async Task WriteResponseAsync(
        Stream stream, string status, string title, string message, CancellationToken cancellationToken)
    {
        var html =
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>SpotifyByYear</title></head>" +
            "<body style=\"font-family: sans-serif; margin: 3rem;\">" +
            $"<h2>{WebUtility.HtmlEncode(title)}</h2><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(head, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
