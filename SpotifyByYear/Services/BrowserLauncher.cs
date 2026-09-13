using System;
using System.Diagnostics;

namespace SpotifyByYear.Services;

/// <summary>Opens the Spotify sign-in page in the user's default browser.</summary>
internal static class BrowserLauncher
{
    private const string AllowedHost = "accounts.spotify.com";

    public static void Open(Uri uri)
    {
        // Shell-executing a string can launch arbitrary programs, so only ever hand over
        // an absolute https URL on Spotify's accounts host.
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Refusing to open non-Spotify URL: {uri}", nameof(uri));
        }

        var url = uri.AbsoluteUri;

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else
        {
            var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            var startInfo = new ProcessStartInfo(opener);
            startInfo.ArgumentList.Add(url);
            Process.Start(startInfo);
        }
    }
}
