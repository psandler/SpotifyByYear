using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using SpotifyByYear.Models;

namespace SpotifyByYear.Services;

/// <summary>
/// Parses a raw <c>GET /playlists/{id}/items</c> page. Working from the raw JSON (rather than the
/// library's typed models) means every field Spotify sends is kept, including ones the library doesn't model.
/// </summary>
public static class PlaylistItemParser
{
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        // Display only: keep accented characters readable instead of \u escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<PlaylistTrackInfo> ParsePage(string json, int firstPosition)
    {
        using var doc = JsonDocument.Parse(json);
        var results = new List<PlaylistTrackInfo>();

        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        var position = firstPosition;
        foreach (var entry in items.EnumerateArray())
        {
            results.Add(ParseEntry(entry, ++position));
        }

        return results;
    }

    private static PlaylistTrackInfo ParseEntry(JsonElement entry, int position)
    {
        // Feb 2026 renamed "track" to "item"; accept either.
        var item = GetObject(entry, "item") ?? GetObject(entry, "track");

        var type = GetString(item, "type") ?? "unknown";
        var name = GetString(item, "name") ?? "(unavailable)";
        var isLocal = GetBool(entry, "is_local") || GetBool(item, "is_local");

        string artists;
        string primaryArtist;
        string album;
        string? albumType;
        string? releaseDate;
        string? precision;

        if (type == "episode")
        {
            var show = GetObject(item, "show");
            artists = primaryArtist = GetString(show, "publisher") ?? "";
            album = GetString(show, "name") ?? "";
            albumType = null;
            releaseDate = GetString(item, "release_date");
            precision = GetString(item, "release_date_precision");
        }
        else
        {
            var artistNames = GetNames(item, "artists");
            artists = string.Join(", ", artistNames);
            primaryArtist = artistNames.FirstOrDefault() ?? "";
            var albumElement = GetObject(item, "album");
            album = GetString(albumElement, "name") ?? "";
            albumType = GetString(albumElement, "album_type");
            releaseDate = GetString(albumElement, "release_date");
            precision = GetString(albumElement, "release_date_precision");
        }

        DateTimeOffset? addedAt = GetString(entry, "added_at") is { } added && DateTimeOffset.TryParse(added, out var parsed)
            ? parsed
            : null;

        return new PlaylistTrackInfo(
            position,
            type,
            GetString(item, "id"),
            GetString(GetObject(item, "external_ids"), "isrc"),
            name,
            artists,
            primaryArtist,
            album,
            albumType,
            string.IsNullOrEmpty(releaseDate) ? null : releaseDate,
            precision,
            isLocal,
            addedAt,
            JsonSerializer.Serialize(entry, IndentedJson));
    }

    private static JsonElement? GetObject(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? GetString(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static List<string> GetNames(JsonElement? element, string arrayName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } e ||
            !e.TryGetProperty(arrayName, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Select(a => GetString(a, "name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();
    }
}
