using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SpotifyByYear.Models;

namespace SpotifyByYear.ViewModels;

/// <summary>One row in the track list: the playlist entry plus its release-year lookup state.</summary>
public partial class TrackRowViewModel : ViewModelBase
{
    public TrackRowViewModel(PlaylistTrackInfo info)
    {
        Info = info;
    }

    public PlaylistTrackInfo Info { get; }

    [ObservableProperty]
    public partial ResolvedReleaseYear? Resolution { get; set; }

    [ObservableProperty]
    public partial bool IsResolving { get; set; }

    public string YearText => Resolution?.Year?.ToString() ?? (IsResolving ? "…" : "?");

    public string Subtitle
    {
        get
        {
            var subtitle = $"{Info.Artists} · {YearText}";
            if (Resolution is { Year: not null } resolution)
            {
                subtitle += $" ({resolution.Source})";
            }

            if (Info.IsLocal)
            {
                subtitle += " · local file";
            }
            else if (Info.Type != "track")
            {
                subtitle += $" · {Info.Type}";
            }

            return subtitle;
        }
    }

    public string AlbumDateText => Info.ReleaseDate is null
        ? "Album release date: none"
        : $"Album released {Info.ReleaseDate} (precision: {Info.ReleaseDatePrecision ?? "unknown"}) · album type: {Info.AlbumType ?? "unknown"}";

    public string ResolvedText
    {
        get
        {
            if (!Info.CanResolveYear)
            {
                return "Original release year: can't be looked up (local file or episode)";
            }

            if (Resolution is null)
            {
                return IsResolving ? "Original release year: looking up…" : "Original release year: not looked up yet";
            }

            var text = $"Original release year: {Resolution.Year?.ToString() ?? "unknown"} (from {Resolution.Source})";
            if (Resolution.Entry.IsLiveVersion)
            {
                text += " · live version: uses the live recording's date";
            }
            if (Resolution.Override?.Note is { Length: > 0 } note)
            {
                text += $" · note: {note}";
            }

            return Resolution.SourcesDisagree ? text + " · ⚠ MusicBrainz and Spotify search disagree" : text;
        }
    }

    public string MusicBrainzText => Describe("MusicBrainz", Resolution?.Entry.MusicBrainz);

    public string SpotifySearchText => Describe("Spotify search", Resolution?.Entry.SpotifySearch);

    public string CandidatesText
    {
        get
        {
            if (Resolution is null)
            {
                return "";
            }

            var musicBrainz = Resolution.Entry.MusicBrainz?.Candidates ?? [];
            var spotify = Resolution.Entry.SpotifySearch?.Candidates ?? [];
            return "MusicBrainz matches (earliest first):\n" +
                   (musicBrainz.Count == 0 ? "  none" : string.Join("\n", musicBrainz.Select(c => "  " + c))) +
                   "\n\nSpotify search matches, compilations excluded (earliest first):\n" +
                   (spotify.Count == 0 ? "  none" : string.Join("\n", spotify.Select(c => "  " + c)));
        }
    }

    partial void OnResolutionChanged(ResolvedReleaseYear? value) => NotifyDerivedChanged();

    partial void OnIsResolvingChanged(bool value) => NotifyDerivedChanged();

    private void NotifyDerivedChanged()
    {
        OnPropertyChanged(nameof(YearText));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ResolvedText));
        OnPropertyChanged(nameof(MusicBrainzText));
        OnPropertyChanged(nameof(SpotifySearchText));
        OnPropertyChanged(nameof(CandidatesText));
    }

    private static string Describe(string source, SourceResult? result) => result switch
    {
        null => $"{source}: not checked",
        { Error: { } error } => $"{source}: lookup failed ({error}); will retry",
        { Year: null } => $"{source}: no match (checked {result.CheckedAt.LocalDateTime:d})",
        _ => $"{source}: {result.Year} via {result.Method} · {result.MatchDescription}",
    };
}
