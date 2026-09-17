# SpotifyByYear

A desktop app that reads your Spotify playlists and works out the **original release year** of every song, so they can be sorted into new playlists by year.

Built with .NET 10 and [Avalonia](https://avaloniaui.net/). A personal hobby project.

## Why the year needs work

Spotify only tells you the release date of the *album* a song is on. For compilations and remasters that's the wrong year: "Rich Girl" on *70s 100 Hits* shows 2010, but the song is from 1976.

SpotifyByYear looks each song up and picks the year like this:

1. **Your own correction**, if you've entered one, always wins.
2. **[MusicBrainz](https://musicbrainz.org/)**: looked up by the song's ISRC (a standard recording ID Spotify provides), or by title and artist when that fails or the song is a remaster. The earliest release date wins.
3. **Spotify search**: the earliest non-compilation album with the same title and artist.
4. The album your copy is on sets a ceiling, since a song can't come out after an album that contains it.

Some deliberate choices:

- **Re-recordings** such as "(Taylor's Version)" get the original song's year.
- **Live versions** get the date of the live recording, not the studio original.
- **Local files and podcast episodes** are skipped.

## Current status

Work in progress. So far the app can:

- Sign in to Spotify and list the playlists you own
- Browse a playlist's songs and see every field Spotify returns for a song, as raw JSON
- Look up the original release year of every song in the background, with progress, and cache the results

Creating the by-year playlists is still to come. See [documents/TODO.md](documents/TODO.md) for the plan.

## Requirements

- Windows. Avalonia is cross-platform, but the app has only been tried on Windows.
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A Spotify account with **Premium**. Since February 2026, Spotify requires the owner of a Development Mode app to have Premium.
- Your own app in the Spotify Developer Dashboard (steps below). Development Mode apps can be used by at most 5 Spotify accounts.

## Setup

### 1. Create a Spotify app

1. Go to the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) and create an app.
2. When asked which APIs the app uses, choose **Web API**.
3. Add this redirect URI exactly as shown:

   ```
   http://127.0.0.1:5543/callback
   ```

   Use `127.0.0.1`. Spotify rejects `localhost`.
4. Copy the app's **Client ID**. No client secret is needed; the app signs in with PKCE.

### 2. Add your Client ID

Copy the example settings file and fill in your Client ID:

```bash
cp SpotifyByYear/appsettings.Local.example.json SpotifyByYear/appsettings.Local.json
```

```json
{
  "Spotify": {
    "ClientId": "your-client-id",
    "RedirectUri": "http://127.0.0.1:5543/callback"
  }
}
```

`appsettings.Local.json` is git-ignored and copied next to the app when you build.

### 3. Build and run

```bash
dotnet build SpotifyByYear.slnx
```

```bash
dotnet run --project SpotifyByYear
```

Click **Load playlists**. The first time, your browser opens Spotify's sign-in page; approve it and return to the app. After that the app signs in on its own, and starts looking up release years for your whole library.

The first full lookup is slow, roughly 30 to 90 minutes for about 900 songs, because MusicBrainz allows only one request per second. It runs in the background while you browse, can be stopped and resumed, and every result is cached, so later runs are instant.

## Tests

```bash
dotnet test --solution SpotifyByYear.slnx
```

- **Offline tests** use fakes and never touch the network, your Spotify account or your saved data.
- **Live tests** against the real MusicBrainz and Spotify services are skipped unless you turn them on. The Spotify one only runs with a saved login from the app, and never opens a browser.

  ```powershell
  $env:SPOTIFYBYYEAR_LIVE_TESTS = "1"; dotnet test --solution SpotifyByYear.slnx
  ```

- `global.json` switches `dotnet test` to Microsoft.Testing.Platform, which xUnit v3 needs on the .NET 10 SDK. That's why the command uses `--solution`.
- Close the app before building or testing, since a running app locks its files.

## Where your data is stored

Everything lives under `%LOCALAPPDATA%\SpotifyByYear\`, never in the repo:

| File | What it holds |
|---|---|
| `token.json` | Your Spotify login. Delete it to sign in again. |
| `release-years.json` | Cached year lookups. Safe to delete; it's rebuilt. |
| `year-overrides.json` | Your manual year corrections. Keep this one. |

To correct a year by hand, add the song's Spotify track ID to `year-overrides.json`:

```json
{
  "overrides": {
    "3IMwTeQ15AjQpNi2IcF5jk": { "year": 1977, "note": "Rich Girl, single release" }
  }
}
```

The track ID is the `id` field in the song's raw JSON in the app, or the last part of its Spotify link.

## Project layout

```
SpotifyByYear/          the Avalonia app (Views, ViewModels, Models, Services)
SpotifyByYear.Tests/    xUnit v3 tests
documents/TODO.md       plan and decisions
CLAUDE.md               notes for Claude Code, which helps build this project
```

## Built with

- [Avalonia](https://avaloniaui.net/) and [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [SpotifyAPI-NET](https://github.com/JohnnyCrazy/SpotifyAPI-NET) for the Spotify Web API
- Release data from [MusicBrainz](https://musicbrainz.org/)
- [xUnit](https://xunit.net/) for tests
