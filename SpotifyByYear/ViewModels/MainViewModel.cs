using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpotifyByYear.Models;
using SpotifyByYear.Services;

namespace SpotifyByYear.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ISpotifyService _spotify;
    private readonly ReleaseYearResolver _resolver;
    private readonly Dictionary<string, IReadOnlyList<PlaylistTrackInfo>> _tracksCache = new();
    private CancellationTokenSource? _tracksCts;
    private bool _isBulkUpdating;

    /// <summary>Used by the XAML designer only.</summary>
    public MainViewModel() : this(CreateDesignServices())
    {
    }

    public MainViewModel(ISpotifyService spotify, ReleaseYearResolver resolver)
    {
        _spotify = spotify;
        _resolver = resolver;
    }

    private MainViewModel((ISpotifyService Spotify, ReleaseYearResolver Resolver) services)
        : this(services.Spotify, services.Resolver)
    {
    }

    public ObservableCollection<PlaylistItemViewModel> Playlists { get; } = [];

    public ObservableCollection<TrackRowViewModel> Tracks { get; } = [];

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Not connected. Click \"Load playlists\" to sign in to Spotify.";

    [ObservableProperty]
    public partial PlaylistItemViewModel? SelectedPlaylist { get; set; }

    [ObservableProperty]
    public partial TrackRowViewModel? SelectedTrack { get; set; }

    [ObservableProperty]
    public partial string TracksStatus { get; set; } = "Select a playlist to see its tracks.";

    /// <summary>
    /// Backs the "Select all" checkbox: true when every playlist is selected, false when none are,
    /// null (indeterminate) when some are. Setting it selects or deselects everything.
    /// </summary>
    public bool? AllSelected
    {
        get
        {
            var selected = Playlists.Count(p => p.IsSelected);
            if (selected == 0)
            {
                return false;
            }

            return selected == Playlists.Count ? true : null;
        }
        set
        {
            var select = value == true;
            _isBulkUpdating = true;
            try
            {
                foreach (var playlist in Playlists)
                {
                    playlist.IsSelected = select;
                }
            }
            finally
            {
                _isBulkUpdating = false;
            }

            OnSelectionChanged();
        }
    }

    public string SelectionSummary => $"{Playlists.Count(p => p.IsSelected)} of {Playlists.Count} selected";

    /// <summary>Saves pending cache writes; call on shutdown.</summary>
    public void FlushCaches() => _resolver.Flush();

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task LoadPlaylistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_spotify.IsConnected)
            {
                StatusText = "Connecting to Spotify… If a browser tab opens, sign in there.";
                await _spotify.ConnectAsync(cancellationToken);
            }

            StatusText = "Loading playlists…";
            var playlists = await _spotify.GetOwnedPlaylistsAsync(cancellationToken);

            _tracksCache.Clear();
            Playlists.Clear();
            foreach (var playlist in playlists.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Playlists.Add(new PlaylistItemViewModel(playlist, OnSelectionChanged));
            }

            OnSelectionChanged();
            StatusText = $"Signed in as {_spotify.UserDisplayName}. You own {playlists.Count} playlists.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LookUpSelectedTrackAgainAsync()
    {
        if (SelectedTrack is not { Info.CanResolveYear: true } row || row.IsResolving)
        {
            return;
        }

        await ResolveRowAsync(row, force: true, CancellationToken.None);
        _resolver.Flush();
    }

    // Both handlers catch their own exceptions, so the discarded tasks can't fault unobserved.
    partial void OnSelectedPlaylistChanged(PlaylistItemViewModel? value) => _ = LoadTracksAsync(value);

    // The clicked track jumps ahead of the playlist's lookup loop.
    partial void OnSelectedTrackChanged(TrackRowViewModel? value)
    {
        if (value is { Resolution: null, IsResolving: false, Info.CanResolveYear: true })
        {
            _ = ResolveRowAsync(value, force: false, _tracksCts?.Token ?? CancellationToken.None);
        }
    }

    private async Task LoadTracksAsync(PlaylistItemViewModel? playlist)
    {
        // Clicking another playlist abandons the previous load and its lookups.
        _tracksCts?.Cancel();
        var cts = new CancellationTokenSource();
        _tracksCts = cts;

        SelectedTrack = null;
        Tracks.Clear();

        if (playlist is null)
        {
            TracksStatus = "Select a playlist to see its tracks.";
            return;
        }

        try
        {
            var id = playlist.Playlist.Id;
            if (!_tracksCache.TryGetValue(id, out var tracks))
            {
                TracksStatus = $"Loading {playlist.Name}…";
                tracks = await _spotify.GetPlaylistItemsAsync(id, cts.Token);
                _tracksCache[id] = tracks;
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            foreach (var track in tracks)
            {
                Tracks.Add(new TrackRowViewModel(track) { Resolution = _resolver.TryGetCached(track) });
            }

            await ResolvePlaylistYearsAsync(playlist.Name, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection.
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                TracksStatus = $"Error loading {playlist.Name}: {ex.Message}";
            }
        }
        finally
        {
            _resolver.Flush();
        }
    }

    private async Task ResolvePlaylistYearsAsync(string playlistName, CancellationToken cancellationToken)
    {
        var rows = Tracks.ToList();

        void UpdateStatus(bool done)
        {
            var resolved = rows.Count(r => r.Resolution is not null);
            TracksStatus = done
                ? $"{playlistName}: {rows.Count} items · release years done"
                : $"{playlistName}: {rows.Count} items · release years {resolved}/{rows.Count} (looking up, ~1/sec)…";
        }

        UpdateStatus(done: rows.All(r => r.Resolution is not null));

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Resolution is null && !row.IsResolving)
            {
                await ResolveRowAsync(row, force: false, cancellationToken);
                UpdateStatus(done: false);
            }
        }

        UpdateStatus(done: true);
    }

    private async Task ResolveRowAsync(TrackRowViewModel row, bool force, CancellationToken cancellationToken)
    {
        row.IsResolving = true;
        try
        {
            row.Resolution = await _resolver.ResolveAsync(row.Info, force, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Playlist changed; the row stays unresolved.
        }
        catch (Exception ex)
        {
            TracksStatus = $"Release year lookup failed: {ex.Message}";
        }
        finally
        {
            row.IsResolving = false;
        }
    }

    private void OnSelectionChanged()
    {
        if (_isBulkUpdating)
        {
            return;
        }

        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private static (ISpotifyService, ReleaseYearResolver) CreateDesignServices()
    {
        var spotify = new SpotifyService(new TokenStore());
        return (spotify, new ReleaseYearResolver(new ReleaseYearCache(), new MusicBrainzClient(), spotify));
    }
}
