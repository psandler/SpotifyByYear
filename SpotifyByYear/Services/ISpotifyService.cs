using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpotifyByYear.Models;

namespace SpotifyByYear.Services;

public interface ISpotifyService
{
    bool IsConnected { get; }

    /// <summary>Display name of the signed-in user, once connected.</summary>
    string? UserDisplayName { get; }

    /// <summary>
    /// Connects using the stored token, or opens the browser for Spotify sign-in if there isn't a usable one.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>All playlists owned by the signed-in user.</summary>
    Task<IReadOnlyList<PlaylistSummary>> GetOwnedPlaylistsAsync(CancellationToken cancellationToken);

    /// <summary>Every entry in a playlist (all pages), including the raw JSON for each.</summary>
    Task<IReadOnlyList<PlaylistTrackInfo>> GetPlaylistItemsAsync(string playlistId, CancellationToken cancellationToken);

    /// <summary>Track search by title and artist (one page; Dev Mode caps search at 10 results).</summary>
    Task<IReadOnlyList<SpotifySearchHit>> SearchTracksAsync(string title, string artist, CancellationToken cancellationToken);
}
