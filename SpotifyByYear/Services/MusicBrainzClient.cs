using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyByYear.Services;

public sealed record MusicBrainzRecording(
    string Id,
    string Title,
    string? Artist,
    string? FirstReleaseDate,
    string? Disambiguation,
    int? Score);

/// <summary>
/// Minimal MusicBrainz web service client. MusicBrainz allows about 1 request/second per IP and
/// requires a User-Agent with contact info: https://musicbrainz.org/doc/MusicBrainz_API/Rate_Limiting
/// </summary>
public sealed class MusicBrainzClient : IMusicBrainzClient, IDisposable
{
    internal const string UserAgent = "SpotifyByYear/0.1 ( https://github.com/psandler/SpotifyByYear )";
    internal const int MaxAttempts = 4;

    private readonly HttpClient _http;
    private readonly TimeSpan _minInterval;
    private readonly TimeSpan _backoffUnit;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Earliest time the next request may start. Guarded by _gate.
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    public MusicBrainzClient()
        : this(new HttpClientHandler(), minInterval: TimeSpan.FromMilliseconds(1100), backoffUnit: TimeSpan.FromSeconds(5))
    {
    }

    /// <summary>For tests: a fake handler and short delays.</summary>
    internal MusicBrainzClient(HttpMessageHandler handler, TimeSpan minInterval, TimeSpan backoffUnit)
    {
        _minInterval = minInterval;
        _backoffUnit = backoffUnit;
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://musicbrainz.org/ws/2/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    public async Task<IReadOnlyList<MusicBrainzRecording>> LookupByIsrcAsync(string isrc, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync($"isrc/{Uri.EscapeDataString(isrc)}?fmt=json", cancellationToken);
        return doc is null ? [] : ParseRecordings(doc.RootElement);
    }

    /// <summary>Up to 100 results, since live/reissue recordings crowd the top of the list.</summary>
    public async Task<IReadOnlyList<MusicBrainzRecording>> SearchRecordingsAsync(
        string title, string artist, CancellationToken cancellationToken)
    {
        var query = $"recording:{Quote(title)} AND artist:{Quote(artist)}";
        using var doc = await GetJsonAsync(
            $"recording?query={Uri.EscapeDataString(query)}&fmt=json&limit=100", cancellationToken);
        return doc is null ? [] : ParseRecordings(doc.RootElement);
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }

    private async Task<JsonDocument?> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;

            // One request at a time, spaced at least _minInterval apart.
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var wait = _nextRequestAt - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken);
                }

                try
                {
                    response = await _http.GetAsync(relativeUrl, cancellationToken);
                }
                finally
                {
                    _nextRequestAt = DateTimeOffset.UtcNow + _minInterval;
                }

                // MusicBrainz answers 503 when rate limited. Back off for every caller, not just this request.
                if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests &&
                    attempt < MaxAttempts)
                {
                    var backoff = response.Headers.RetryAfter?.Delta ?? _backoffUnit * attempt;
                    _nextRequestAt = DateTimeOffset.UtcNow + backoff;
                    response.Dispose();
                    continue;
                }
            }
            finally
            {
                _gate.Release();
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
        }
    }

    private static List<MusicBrainzRecording> ParseRecordings(JsonElement root)
    {
        if (!root.TryGetProperty("recordings", out var recordings) || recordings.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return recordings.EnumerateArray()
            .Where(r => GetString(r, "id") is not null)
            .Select(r => new MusicBrainzRecording(
                GetString(r, "id")!,
                GetString(r, "title") ?? "",
                JoinArtistCredit(r),
                GetString(r, "first-release-date") is { Length: > 0 } date ? date : null,
                GetString(r, "disambiguation") is { Length: > 0 } disambiguation ? disambiguation : null,
                r.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number ? score.GetInt32() : null))
            .ToList();
    }

    private static string? JoinArtistCredit(JsonElement recording)
    {
        if (!recording.TryGetProperty("artist-credit", out var credits) || credits.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return string.Concat(credits.EnumerateArray().Select(c => GetString(c, "name") + GetString(c, "joinphrase")));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Lucene phrase: inside quotes only backslash and quote need escaping.</summary>
    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
