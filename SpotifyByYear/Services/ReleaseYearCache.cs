using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using SpotifyByYear.Models;

namespace SpotifyByYear.Services;

/// <summary>
/// File-based cache of release-year lookups plus hand-entered overrides, under
/// <c>%LOCALAPPDATA%\SpotifyByYear\</c>:
/// <list type="bullet">
/// <item><c>release-years.json</c>: lookup results, safe to delete (it is rebuilt).</item>
/// <item><c>year-overrides.json</c>: manual corrections, kept separate so they survive a cache wipe.</item>
/// </list>
/// </summary>
public sealed class ReleaseYearCache
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _cachePath;
    private readonly string _overridesPath;
    private readonly Lock _lock = new();
    private Dictionary<string, ReleaseYearEntry> _entries = new();
    private Dictionary<string, YearOverride> _overrides = new();
    private bool _loaded;
    private bool _dirty;

    public ReleaseYearCache(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpotifyByYear");
        _cachePath = Path.Combine(directory, "release-years.json");
        _overridesPath = Path.Combine(directory, "year-overrides.json");
    }

    public ReleaseYearEntry? Get(string trackId)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _entries.GetValueOrDefault(trackId);
        }
    }

    public void Set(ReleaseYearEntry entry)
    {
        lock (_lock)
        {
            EnsureLoaded();
            _entries[entry.TrackId] = entry;
            _dirty = true;
        }
    }

    public YearOverride? GetOverride(string trackId)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _overrides.GetValueOrDefault(trackId);
        }
    }

    /// <summary>Writes the cache if anything changed (temp file + rename, so a crash can't truncate it).</summary>
    public void Save()
    {
        string json;
        lock (_lock)
        {
            if (!_loaded || !_dirty)
            {
                return;
            }

            json = JsonSerializer.Serialize(new CacheFile { SchemaVersion = SchemaVersion, Entries = _entries }, JsonOptions);
            _dirty = false;
        }

        WriteAtomically(_cachePath, json);
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        _entries = LoadCacheEntries();
        _overrides = LoadOverrides();
        _loaded = true;
    }

    private Dictionary<string, ReleaseYearEntry> LoadCacheEntries()
    {
        if (!File.Exists(_cachePath))
        {
            return new();
        }

        try
        {
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(_cachePath), JsonOptions);
            if (file?.SchemaVersion == SchemaVersion && file.Entries is not null)
            {
                return file.Entries;
            }
        }
        catch (JsonException)
        {
            // Fall through: keep the unreadable file aside rather than overwriting it.
        }

        File.Move(_cachePath, _cachePath + $".unreadable-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
        return new();
    }

    private Dictionary<string, YearOverride> LoadOverrides()
    {
        if (!File.Exists(_overridesPath))
        {
            // Create an empty file so it's easy to find and edit.
            WriteAtomically(_overridesPath, JsonSerializer.Serialize(new OverridesFile(), JsonOptions));
            return new();
        }

        try
        {
            return JsonSerializer.Deserialize<OverridesFile>(File.ReadAllText(_overridesPath), JsonOptions)?.Overrides ?? new();
        }
        catch (JsonException ex)
        {
            // Hand-edited file: surface the mistake instead of silently ignoring corrections.
            throw new InvalidOperationException($"{_overridesPath} is not valid JSON: {ex.Message}", ex);
        }
    }

    private static void WriteAtomically(string path, string contents)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, path, overwrite: true);
    }

    private sealed class CacheFile
    {
        public int SchemaVersion { get; set; }
        public Dictionary<string, ReleaseYearEntry>? Entries { get; set; }
    }

    /// <summary>Format: { "overrides": { "&lt;spotify track id&gt;": { "year": 1977, "note": "Rich Girl" } } }</summary>
    private sealed class OverridesFile
    {
        public Dictionary<string, YearOverride> Overrides { get; set; } = new();
    }
}
