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
    private readonly Dictionary<string, IReadOnlyList<PlaylistTrackInfo>> _tracksCache = new();
    private CancellationTokenSource? _tracksCts;
    private bool _isBulkUpdating;

    /// <summary>Used by the XAML designer only.</summary>
    public MainViewModel() : this(new SpotifyService(new TokenStore()))
    {
    }

    public MainViewModel(ISpotifyService spotify)
    {
        _spotify = spotify;
    }

    public ObservableCollection<PlaylistItemViewModel> Playlists { get; } = [];

    public ObservableCollection<PlaylistTrackInfo> Tracks { get; } = [];

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Not connected. Click \"Load playlists\" to sign in to Spotify.";

    [ObservableProperty]
    public partial PlaylistItemViewModel? SelectedPlaylist { get; set; }

    [ObservableProperty]
    public partial PlaylistTrackInfo? SelectedTrack { get; set; }

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

    // LoadTracksAsync handles its own exceptions, so the discarded task can't fault unobserved.
    partial void OnSelectedPlaylistChanged(PlaylistItemViewModel? value) => _ = LoadTracksAsync(value);

    private async Task LoadTracksAsync(PlaylistItemViewModel? playlist)
    {
        // Clicking another playlist abandons the previous load.
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
                Tracks.Add(track);
            }

            TracksStatus = $"{playlist.Name}: {tracks.Count} items";
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
}
