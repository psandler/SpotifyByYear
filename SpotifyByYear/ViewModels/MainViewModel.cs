using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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

    // Track ids being looked up right now, so the library pass and a clicked track don't duplicate work.
    private readonly HashSet<string> _inFlightLookups = [];

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
    public partial string YearsStatus { get; set; } = "";

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
            return;
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            return;
        }

        // Look up release years for the whole library in the background. Does nothing while a pass
        // is already running, and that pass covers the same tracks anyway.
        ResolveAllYearsCommand.Execute(null);
    }

    /// <summary>Looks up the release year of every track in every owned playlist, skipping cached ones.</summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ResolveAllYearsAsync(CancellationToken cancellationToken)
    {
        if (Playlists.Count == 0)
        {
            YearsStatus = "Load playlists first.";
            return;
        }

        var tracks = new List<PlaylistTrackInfo>();
        var done = 0;

        try
        {
            var seen = new HashSet<string>();
            for (var i = 0; i < Playlists.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                YearsStatus = $"Reading playlists… ({i + 1}/{Playlists.Count})";
                foreach (var track in await GetPlaylistTracksAsync(Playlists[i], cancellationToken))
                {
                    // The same song in several playlists is only looked up once.
                    if (track.CanResolveYear && seen.Add(track.TrackId!))
                    {
                        tracks.Add(track);
                    }
                }
            }

            done = tracks.Count(t => _resolver.TryGetCached(t) is not null);
            YearsStatus = Progress(done, tracks.Count, TimeSpan.Zero, lookedUp: 0);

            var stopwatch = Stopwatch.StartNew();
            var lookedUp = 0;
            foreach (var track in tracks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_resolver.TryGetCached(track) is { } cached)
                {
                    ApplyResolution(track.TrackId!, cached);
                    continue;
                }

                await ResolveTrackAsync(track, force: false, cancellationToken);
                lookedUp++;
                done++;
                YearsStatus = Progress(done, tracks.Count, stopwatch.Elapsed, lookedUp);
            }

            YearsStatus = $"Release years: {tracks.Count} songs done.";
        }
        catch (OperationCanceledException)
        {
            YearsStatus = $"Release years: stopped at {done} of {tracks.Count} songs. \"Look up all years\" continues where it left off.";
        }
        catch (Exception ex)
        {
            YearsStatus = $"Release year lookup failed: {ex.Message}";
        }
        finally
        {
            _resolver.Flush();
        }
    }

    [RelayCommand]
    private async Task LookUpSelectedTrackAgainAsync()
    {
        if (SelectedTrack is not { Info.CanResolveYear: true } row)
        {
            return;
        }

        await ResolveTrackAsync(row.Info, force: true, CancellationToken.None);
        _resolver.Flush();
    }

    // Both handlers catch their own exceptions, so the discarded tasks can't fault unobserved.
    partial void OnSelectedPlaylistChanged(PlaylistItemViewModel? value) => _ = LoadTracksAsync(value);

    // A clicked track jumps ahead of the library pass.
    partial void OnSelectedTrackChanged(TrackRowViewModel? value)
    {
        if (value is { Resolution: null, Info.CanResolveYear: true })
        {
            _ = ResolveTrackAsync(value.Info, force: false, _tracksCts?.Token ?? CancellationToken.None);
        }
    }

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
            TracksStatus = $"Loading {playlist.Name}…";
            var tracks = await GetPlaylistTracksAsync(playlist, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            foreach (var track in tracks)
            {
                Tracks.Add(new TrackRowViewModel(track) { Resolution = _resolver.TryGetCached(track) });
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

    private async Task<IReadOnlyList<PlaylistTrackInfo>> GetPlaylistTracksAsync(
        PlaylistItemViewModel playlist, CancellationToken cancellationToken)
    {
        var id = playlist.Playlist.Id;
        if (_tracksCache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var tracks = await _spotify.GetPlaylistItemsAsync(id, cancellationToken);
        _tracksCache[id] = tracks;
        return tracks;
    }

    private async Task ResolveTrackAsync(PlaylistTrackInfo track, bool force, CancellationToken cancellationToken)
    {
        if (track.TrackId is not { } trackId || !_inFlightLookups.Add(trackId))
        {
            return; // already being looked up
        }

        SetRowsResolving(trackId, true);
        try
        {
            ApplyResolution(trackId, await _resolver.ResolveAsync(track, force, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // Playlist changed, or the lookup pass was stopped.
        }
        catch (Exception ex)
        {
            YearsStatus = $"Release year lookup failed: {ex.Message}";
        }
        finally
        {
            _inFlightLookups.Remove(trackId);
            SetRowsResolving(trackId, false);
        }
    }

    private void ApplyResolution(string trackId, ResolvedReleaseYear resolution)
    {
        foreach (var row in Tracks)
        {
            if (row.Info.TrackId == trackId)
            {
                row.Resolution = resolution;
            }
        }
    }

    private void SetRowsResolving(string trackId, bool isResolving)
    {
        foreach (var row in Tracks)
        {
            if (row.Info.TrackId == trackId)
            {
                row.IsResolving = isResolving;
            }
        }
    }

    private static string Progress(int done, int total, TimeSpan elapsed, int lookedUp)
    {
        var text = $"Release years: {done}/{total} songs";
        if (lookedUp > 0 && done < total)
        {
            var remaining = elapsed / lookedUp * (total - done);
            text += remaining.TotalMinutes >= 1
                ? $" · about {Math.Ceiling(remaining.TotalMinutes)} min left"
                : " · nearly done";
        }

        return text;
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
