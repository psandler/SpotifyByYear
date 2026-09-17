using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using SpotifyByYear.Models;

namespace SpotifyByYear.Services;

/// <summary>
/// Works out a track's original release year from MusicBrainz and Spotify search, caching every result.
/// </summary>
public sealed class ReleaseYearResolver
{
    /// <summary>Bump when matching rules change so cached entries are looked up again.</summary>
    public const int LogicVersion = 2; // 2: live versions use the live recording's date

    private static readonly TimeSpan NoMatchRetryAfter = TimeSpan.FromDays(30);
    private const int SaveEvery = 25;
    private const int MaxCandidates = 10;

    private readonly ReleaseYearCache _cache;
    private readonly IMusicBrainzClient _musicBrainz;
    private readonly ISpotifyService _spotify;
    private int _unsavedLookups;

    public ReleaseYearResolver(ReleaseYearCache cache, IMusicBrainzClient musicBrainz, ISpotifyService spotify)
    {
        _cache = cache;
        _musicBrainz = musicBrainz;
        _spotify = spotify;
    }

    /// <summary>Resolution from the cache alone, or null if the track still needs a lookup.</summary>
    public ResolvedReleaseYear? TryGetCached(PlaylistTrackInfo track)
    {
        if (!track.CanResolveYear)
        {
            return new ResolvedReleaseYear(CreateEntry(track), null);
        }

        var entry = _cache.Get(track.TrackId!);
        if (entry is null || entry.LogicVersion != LogicVersion ||
            NeedsLookup(entry.MusicBrainz) || NeedsLookup(entry.SpotifySearch))
        {
            return null;
        }

        return new ResolvedReleaseYear(entry, _cache.GetOverride(track.TrackId!));
    }

    public async Task<ResolvedReleaseYear> ResolveAsync(PlaylistTrackInfo track, bool force, CancellationToken cancellationToken)
    {
        if (!track.CanResolveYear)
        {
            return new ResolvedReleaseYear(CreateEntry(track), null);
        }

        var trackId = track.TrackId!;
        var entry = force ? null : _cache.Get(trackId);
        if (entry is null || entry.LogicVersion != LogicVersion)
        {
            entry = CreateEntry(track);
        }
        else
        {
            UpdateTrackFields(entry, track);
        }

        var lookedUp = false;

        if (NeedsLookup(entry.MusicBrainz))
        {
            entry.MusicBrainz = await LookUpMusicBrainzAsync(track, cancellationToken);
            lookedUp = true;
        }

        if (NeedsLookup(entry.SpotifySearch))
        {
            entry.SpotifySearch = await LookUpSpotifySearchAsync(track, cancellationToken);
            lookedUp = true;
        }

        _cache.Set(entry);
        if (lookedUp && ++_unsavedLookups >= SaveEvery)
        {
            Flush();
        }

        return new ResolvedReleaseYear(entry, _cache.GetOverride(trackId));
    }

    public void Flush()
    {
        _unsavedLookups = 0;
        _cache.Save();
    }

    private async Task<SourceResult> LookUpMusicBrainzAsync(PlaylistTrackInfo track, CancellationToken cancellationToken)
    {
        try
        {
            var isLive = TrackMatching.IsLiveVersion(track.Name);
            var liveYear = TrackMatching.LiveYearFromTitle(track.Name);
            var matches = new List<(MusicBrainzRecording Recording, string Method)>();

            if (!string.IsNullOrEmpty(track.Isrc))
            {
                // The ISRC identifies this exact recording, so a live copy may match a live recording.
                var byIsrc = await _musicBrainz.LookupByIsrcAsync(track.Isrc, cancellationToken);
                matches.AddRange(byIsrc
                    .Where(r => TrackMatching.TitlesMatch(r.Title, track.Name) &&
                                (isLive || !TrackMatching.IsAlternateVersion(r.Disambiguation)))
                    .Select(r => (r, "isrc")));
            }

            // Search when the ISRC gave nothing usable, or when a non-live copy is a remaster/mix/etc.
            // (those often have their own MusicBrainz recording with a later date than the original).
            if (!matches.Any(m => HasYear(m.Recording)) || (!isLive && TrackMatching.HasVersionDecoration(track.Name)))
            {
                var bySearch = await _musicBrainz.SearchRecordingsAsync(
                    TrackMatching.CleanTitle(track.Name), track.PrimaryArtist, cancellationToken);
                var sameSong = bySearch
                    .Where(r => (r.Score ?? 0) >= 90 &&
                                TrackMatching.TitlesMatch(r.Title, track.Name) &&
                                TrackMatching.ArtistsMatch(track.PrimaryArtist, r.Artist))
                    .ToList();

                matches.AddRange(isLive
                    // The earliest live recording could be any concert, so only accept one from the year in the title.
                    ? sameSong
                        .Where(r => TrackMatching.IsLiveDisambiguation(r.Disambiguation) &&
                                    liveYear is int year &&
                                    (ReleaseDates.ParseYear(r.FirstReleaseDate) == year ||
                                     r.Disambiguation!.Contains($"{year}", StringComparison.Ordinal)))
                        .Select(r => (r, "search"))
                    : sameSong
                        .Where(r => !TrackMatching.IsAlternateVersion(r.Disambiguation))
                        .Select(r => (r, "search")));
            }

            var ordered = matches
                .Where(m => HasYear(m.Recording))
                .OrderBy(m => m.Recording.FirstReleaseDate, StringComparer.Ordinal)
                .ToList();

            var result = new SourceResult
            {
                CheckedAt = DateTimeOffset.UtcNow,
                Candidates = ordered
                    .DistinctBy(m => m.Recording.Id)
                    .Take(MaxCandidates)
                    .Select(m => $"{m.Recording.FirstReleaseDate} · {m.Recording.Title} · {m.Recording.Artist}" +
                                 (m.Recording.Disambiguation is { } d ? $" ({d})" : "") +
                                 $" · via {m.Method} · https://musicbrainz.org/recording/{m.Recording.Id}")
                    .ToList(),
            };

            if (ordered.FirstOrDefault() is { Recording: not null } best)
            {
                result.Year = ReleaseDates.ParseYear(best.Recording.FirstReleaseDate);
                result.Method = best.Method;
                result.MatchId = best.Recording.Id;
                result.MatchDescription =
                    $"{best.Recording.Title} — {best.Recording.Artist}, first released {best.Recording.FirstReleaseDate}";
            }

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new SourceResult { CheckedAt = DateTimeOffset.UtcNow, Error = ex.Message };
        }
    }

    private async Task<SourceResult> LookUpSpotifySearchAsync(PlaylistTrackInfo track, CancellationToken cancellationToken)
    {
        try
        {
            // Live copies search for the live track itself (full title), not the studio song.
            var isLive = TrackMatching.IsLiveVersion(track.Name);
            var hits = await _spotify.SearchTracksAsync(
                isLive ? track.Name : TrackMatching.CleanTitle(track.Name), track.PrimaryArtist, cancellationToken);

            var ordered = hits
                .Where(h => ReleaseDates.ParseYear(h.ReleaseDate) is not null &&
                            h.AlbumType != "compilation" &&
                            (isLive ? TrackMatching.TitlesMatchExactly(h.Name, track.Name) : TrackMatching.TitlesMatch(h.Name, track.Name)) &&
                            h.Artists.Any(a => TrackMatching.ArtistsMatch(track.PrimaryArtist, a)))
                .OrderBy(h => h.ReleaseDate, StringComparer.Ordinal)
                .ToList();

            var result = new SourceResult
            {
                CheckedAt = DateTimeOffset.UtcNow,
                Candidates = ordered
                    .Take(MaxCandidates)
                    .Select(h => $"{h.ReleaseDate} · {h.Name} · {string.Join(", ", h.Artists)} · on \"{h.AlbumName}\" ({h.AlbumType})" +
                                 $" · https://open.spotify.com/album/{h.AlbumId}")
                    .ToList(),
            };

            if (ordered.FirstOrDefault() is { } best)
            {
                result.Year = ReleaseDates.ParseYear(best.ReleaseDate);
                result.Method = "search";
                result.MatchId = best.AlbumId;
                result.MatchDescription = $"{best.Name} on \"{best.AlbumName}\" ({best.AlbumType}, released {best.ReleaseDate})";
            }

            return result;
        }
        catch (Exception ex) when (ex is APIException or HttpRequestException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new SourceResult { CheckedAt = DateTimeOffset.UtcNow, Error = ex.Message };
        }
    }

    private static bool NeedsLookup(SourceResult? result) =>
        result is null ||
        result.Error is not null ||
        (result.Year is null && DateTimeOffset.UtcNow - result.CheckedAt > NoMatchRetryAfter);

    private static bool HasYear(MusicBrainzRecording recording) =>
        ReleaseDates.ParseYear(recording.FirstReleaseDate) is not null;

    private static ReleaseYearEntry CreateEntry(PlaylistTrackInfo track)
    {
        var entry = new ReleaseYearEntry { TrackId = track.TrackId ?? "", LogicVersion = LogicVersion };
        UpdateTrackFields(entry, track);
        return entry;
    }

    private static void UpdateTrackFields(ReleaseYearEntry entry, PlaylistTrackInfo track)
    {
        entry.Isrc = track.Isrc;
        entry.Title = track.Name;
        entry.Artists = track.Artists;
        entry.AlbumReleaseDate = track.ReleaseDate;
        entry.AlbumType = track.AlbumType;
        entry.IsLiveVersion = TrackMatching.IsLiveVersion(track.Name);
        entry.TitleYear = TrackMatching.LiveYearFromTitle(track.Name);
    }
}
