using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SpotifyByYear.Models;

namespace SpotifyByYear.ViewModels;

/// <summary>One row in the playlist list: a playlist plus its checkbox state.</summary>
public partial class PlaylistItemViewModel : ViewModelBase
{
    private readonly Action _selectionChanged;

    public PlaylistItemViewModel(PlaylistSummary playlist, Action selectionChanged)
    {
        Playlist = playlist;
        _selectionChanged = selectionChanged;
    }

    public PlaylistSummary Playlist { get; }

    public string Name => Playlist.Name;

    public int TrackCount => Playlist.TrackCount;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}
