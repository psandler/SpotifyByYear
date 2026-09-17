using System;
using System.IO;
using System.Text.Json;
using SpotifyAPI.Web;

namespace SpotifyByYear.Services;

/// <summary>
/// Persists the Spotify token (including the refresh token) under
/// <c>%LOCALAPPDATA%\SpotifyByYear\token.json</c> so the user only signs in once.
/// </summary>
public sealed class TokenStore
{
    private readonly string _path;

    /// <param name="directory">Folder for token.json; defaults to <c>%LOCALAPPDATA%\SpotifyByYear</c>.</param>
    public TokenStore(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpotifyByYear");
        _path = Path.Combine(directory, "token.json");
    }

    public PKCETokenResponse? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PKCETokenResponse>(File.ReadAllText(_path));
        }
        catch (JsonException)
        {
            // A corrupt file just means signing in again.
            return null;
        }
    }

    public void Save(PKCETokenResponse token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(token));
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
