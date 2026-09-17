using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using SpotifyByYear.Models;

namespace SpotifyByYear.Services;

public sealed class SpotifyService : ISpotifyService
{
    // Modify scopes are requested up front so creating year playlists later doesn't force a second sign-in.
    private static readonly string[] RequiredScopes =
    [
        Scopes.PlaylistReadPrivate,
        Scopes.PlaylistReadCollaborative,
        Scopes.PlaylistModifyPrivate,
        Scopes.PlaylistModifyPublic,
    ];

    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private readonly TokenStore _tokenStore;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private SpotifyClient? _client;
    private PrivateUser? _currentUser;

    public SpotifyService(TokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    public bool IsConnected => _client is not null;

    public string? UserDisplayName => _currentUser?.DisplayName ?? _currentUser?.Id;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var settings = SpotifySettings.Load();
        if (await TryConnectWithSavedLoginAsync(settings, cancellationToken))
        {
            return;
        }

        var token = await SignInWithBrowserAsync(settings, cancellationToken);
        _tokenStore.Save(token);
        await CreateClientAsync(settings, token, cancellationToken);
    }

    public async Task<IReadOnlyList<PlaylistSummary>> GetOwnedPlaylistsAsync(CancellationToken cancellationToken)
    {
        if (_client is null || _currentUser is null)
        {
            throw new InvalidOperationException("Not connected to Spotify.");
        }

        IList<FullPlaylist> all;
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            var firstPage = await _client.Playlists.CurrentUsers(
                new PlaylistCurrentUsersRequest { Limit = 50 }, cancellationToken);
            all = await _client.PaginateAll(firstPage, cancellationToken: cancellationToken);
        }
        finally
        {
            _requestLock.Release();
        }

        return all
            .Where(p => p.Id is not null && p.Owner?.Id == _currentUser.Id)
            .Select(p => new PlaylistSummary(p.Id!, p.Name ?? "(untitled)", p.Items?.Total ?? 0))
            .ToList();
    }

    public async Task<IReadOnlyList<PlaylistTrackInfo>> GetPlaylistItemsAsync(string playlistId, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Not connected to Spotify.");
        }

        var results = new List<PlaylistTrackInfo>();
        var request = new PlaylistGetItemsRequest(PlaylistGetItemsRequest.AdditionalTypes.All)
        {
            Limit = 100,
            Offset = 0,
        };

        while (true)
        {
            string body;
            Paging<PlaylistTrack<IPlayableItem>> page;

            // LastResponse is shared client state, so the call and the read must not interleave with other requests.
            await _requestLock.WaitAsync(cancellationToken);
            try
            {
                page = await _client.Playlists.GetPlaylistItems(playlistId, request, cancellationToken);
                body = _client.LastResponse?.Body as string
                    ?? throw new InvalidOperationException("Spotify response body was not available as JSON text.");
            }
            finally
            {
                _requestLock.Release();
            }

            results.AddRange(PlaylistItemParser.ParsePage(body, results.Count));

            var pageCount = page.Items?.Count ?? 0;
            if (page.Next is null || pageCount == 0)
            {
                break;
            }

            request.Offset += pageCount;
        }

        return results;
    }

    public async Task<IReadOnlyList<SpotifySearchHit>> SearchTracksAsync(
        string title, string artist, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Not connected to Spotify.");
        }

        // Field filters narrow the 10 results to the right song; quotes inside values would break the phrase.
        var query = $"track:\"{title.Replace("\"", " ")}\" artist:\"{artist.Replace("\"", " ")}\"";

        SearchResponse response;
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            response = await _client.Search.Item(
                new SearchRequest(SearchRequest.Types.Track, query) { Limit = 10 }, cancellationToken);
        }
        finally
        {
            _requestLock.Release();
        }

        return (response.Tracks?.Items ?? [])
            .Where(t => t is not null)
            .Select(t => new SpotifySearchHit(
                t.Id,
                t.Name ?? "",
                t.Artists?.Select(a => a.Name).Where(n => n is not null).ToList() ?? [],
                t.Album?.Id,
                t.Album?.Name ?? "",
                t.Album?.AlbumType,
                t.Album?.ReleaseDate))
            .ToList();
    }

    private async Task CreateClientAsync(SpotifySettings settings, PKCETokenResponse token, CancellationToken cancellationToken)
    {
        var authenticator = new PKCEAuthenticator(settings.ClientId, token);
        var lastRefreshToken = token.RefreshToken;
        authenticator.TokenRefreshed += (_, refreshed) =>
        {
            lastRefreshToken = KeepRefreshToken(refreshed, lastRefreshToken);
            KeepRefreshToken(authenticator.InitialToken, lastRefreshToken);
            _tokenStore.Save(refreshed);
        };

        var config = SpotifyClientConfig.CreateDefault()
            .WithAuthenticator(authenticator)
            .WithRetryHandler(new SimpleRetryHandler()); // honors 429 Retry-After
        var client = new SpotifyClient(config);

        // Also validates the token (and refreshes it if it has expired).
        _currentUser = await client.UserProfile.Current(cancellationToken);
        _client = client;
    }

    private static async Task<PKCETokenResponse> SignInWithBrowserAsync(SpotifySettings settings, CancellationToken cancellationToken)
    {
        var (verifier, challenge) = PKCEUtil.GenerateCodes(100);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var loginRequest = new LoginRequest(settings.RedirectUri, settings.ClientId, LoginRequest.ResponseType.Code)
        {
            CodeChallengeMethod = "S256",
            CodeChallenge = challenge,
            Scope = RequiredScopes,
            State = state,
        };

        using var listener = LoopbackCallbackListener.Start(settings.RedirectUri);
        BrowserLauncher.Open(loginRequest.ToUri());

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SignInTimeout);

        string code;
        try
        {
            code = await listener.WaitForCodeAsync(state, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for Spotify sign-in to finish in the browser.");
        }

        return await new OAuthClient().RequestToken(
            new PKCETokenRequest(settings.ClientId, code, settings.RedirectUri, verifier), cancellationToken);
    }

    /// <summary>
    /// Connects with the saved login only; never opens a browser. False if there's no usable saved login.
    /// </summary>
    internal async Task<bool> TryConnectWithSavedLoginAsync(CancellationToken cancellationToken) =>
        await TryConnectWithSavedLoginAsync(SpotifySettings.Load(), cancellationToken);

    private async Task<bool> TryConnectWithSavedLoginAsync(SpotifySettings settings, CancellationToken cancellationToken)
    {
        var storedToken = _tokenStore.Load();
        if (storedToken is null || !HasRequiredScopes(storedToken) || !CanStillBeUsed(storedToken))
        {
            return false;
        }

        try
        {
            await CreateClientAsync(settings, storedToken, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is APIException or ArgumentException)
        {
            // Refresh token revoked, missing, or issued for a different Client ID.
            _tokenStore.Clear();
            return false;
        }
    }

    /// <summary>
    /// Spotify doesn't always return a new refresh token on refresh; the previous one stays valid.
    /// Saving the response as-is would wipe it and break the next refresh. Returns the refresh token to keep using.
    /// </summary>
    internal static string KeepRefreshToken(PKCETokenResponse token, string previousRefreshToken)
    {
        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            token.RefreshToken = previousRefreshToken;
            return previousRefreshToken;
        }

        return token.RefreshToken;
    }

    /// <summary>An expired token is only usable if it can be refreshed.</summary>
    internal static bool CanStillBeUsed(PKCETokenResponse token) =>
        !string.IsNullOrEmpty(token.RefreshToken) || !token.IsExpired;

    internal static bool HasRequiredScopes(PKCETokenResponse token)
    {
        var granted = (token.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return RequiredScopes.All(granted.Contains);
    }
}
