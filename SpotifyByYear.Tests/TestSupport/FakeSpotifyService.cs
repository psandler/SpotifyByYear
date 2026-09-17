using SpotifyByYear.Models;
using SpotifyByYear.Services;

namespace SpotifyByYear.Tests.TestSupport;

public sealed class FakeSpotifyService : ISpotifyService
{
    public bool IsConnected { get; private set; }

    public string? UserDisplayName => "Test User";

    public List<PlaylistSummary> Playlists { get; } = [];

    public Dictionary<string, List<PlaylistTrackInfo>> Items { get; } = new();

    public List<SpotifySearchHit> SearchHits { get; set; } = [];

    /// <summary>"&lt;title&gt;|&lt;artist&gt;" for each search.</summary>
    public List<string> SearchQueries { get; } = [];

    public Exception? ConnectException { get; set; }

    public static SpotifySearchHit Hit(
        string releaseDate,
        string albumType = "album",
        string name = "Rich Girl",
        string artist = "Daryl Hall & John Oates") =>
        new("track-" + releaseDate, name, [artist], "album-" + releaseDate, "Album " + releaseDate, albumType, releaseDate);

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (ConnectException is not null)
        {
            return Task.FromException(ConnectException);
        }

        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PlaylistSummary>> GetOwnedPlaylistsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlaylistSummary>>(Playlists);

    public Task<IReadOnlyList<PlaylistTrackInfo>> GetPlaylistItemsAsync(string playlistId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlaylistTrackInfo>>(Items.GetValueOrDefault(playlistId) ?? []);

    public Task<IReadOnlyList<SpotifySearchHit>> SearchTracksAsync(string title, string artist, CancellationToken cancellationToken)
    {
        SearchQueries.Add($"{title}|{artist}");
        return Task.FromResult<IReadOnlyList<SpotifySearchHit>>(SearchHits);
    }
}
