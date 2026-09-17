using System;
using System.IO;
using System.Text.Json;

namespace SpotifyByYear.Services;

/// <summary>
/// Spotify app settings, read from <c>appsettings.Local.json</c> next to the executable.
/// The file is git-ignored; <c>appsettings.Local.example.json</c> shows the expected shape.
/// </summary>
public sealed record SpotifySettings(string ClientId, Uri RedirectUri)
{
    public const string FileName = "appsettings.Local.json";

    private static readonly Uri DefaultRedirectUri = new("http://127.0.0.1:5543/callback");

    /// <param name="directory">Folder containing the settings file; defaults to the app's folder.</param>
    public static SpotifySettings Load(string? directory = null)
    {
        var path = Path.Combine(directory ?? AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Missing {FileName}. Copy appsettings.Local.example.json to {FileName} in the " +
                "SpotifyByYear project folder, fill in your Spotify Client ID, and rebuild.");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("Spotify", out var spotify))
        {
            throw new InvalidOperationException($"{FileName} has no \"Spotify\" section.");
        }

        var clientId = GetString(spotify, "ClientId");
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Contains("paste", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Set Spotify:ClientId in {FileName}.");
        }

        var redirectUri = GetString(spotify, "RedirectUri") is { Length: > 0 } configured
            ? new Uri(configured)
            : DefaultRedirectUri;

        // The callback listener only binds to the loopback interface.
        if (!redirectUri.IsLoopback || redirectUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException(
                $"Spotify:RedirectUri must be an http loopback address such as {DefaultRedirectUri}.");
        }

        return new SpotifySettings(clientId.Trim(), redirectUri);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
