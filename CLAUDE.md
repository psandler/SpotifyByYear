# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Hard rules

- **No git write operations of any kind.** The human does every git write: `init`, `add`, `commit`, `branch`, `checkout`/`switch`, `merge`, `rebase`, `cherry-pick`, `stash`, `tag`, `reset`, `restore`, `rm`, `mv`, `push`, `pull`, `fetch`, `config`, and anything else that changes the repo, index, refs, or remotes. Read-only commands (`status`, `diff`, `log`, `show`, `blame`) are fine. When work is ready to commit, say so and suggest a commit message. Don't run the commit.
- **No GitHub access.** Don't use the `gh` CLI or GitHub API/MCP tools. The human handles everything on GitHub (repo, PRs, issues).
- **Never commit or print secrets.** The Spotify Client ID lives in local config (see below), and tokens live in the user profile. Neither belongs in source.
- **No Aikido in this project.** The Aikido plugin is disabled here via `enabledPlugins` in `.claude/settings.json` (it stays enabled globally for the user's work projects). Don't run Aikido scans or skills in this repo, and don't change the user-level plugin settings.

## Project

SpotifyByYear is a desktop app that reads the user's own Spotify playlists and builds new playlists grouped by **track release year**.

- Sources: all playlists the **user owns**. More sources and features are TBD; the user will explain them as we go.
- "Year" means the song's **original** release year. Spotify only provides `album.release_date` (the album this copy is on), which is wrong for compilations and remasters (e.g. "Rich Girl" shows 2010 instead of 1976). `ReleaseYearResolver` works out the real year:
  1. Manual override from `year-overrides.json` always wins.
  2. MusicBrainz: ISRC lookup; recording search (limit 100, skip live/demo/etc. disambiguations, score ≥ 90) when ISRC has no match or the Spotify title has version text ("- 2011 Remaster"). Earliest `first-release-date` wins.
  3. Spotify search (every track): earliest non-compilation album with a matching title and artist. Used when MusicBrainz has no match.
  4. The playlist copy's album year is an upper bound (a song can't come out after an album containing it).
- **Re-recordings** ("(Taylor's Version)") → the original song's year (user decision). **Live versions** (a "live" marker in the title's version text) → the live recording's date, not the studio original (user decision): ISRC match may be a live recording; MusicBrainz search only accepts live recordings from the year written in the title; then that title year; Spotify search uses the full live title.
- Both sources are always checked and stored so disagreements are visible. Bump `ReleaseYearResolver.LogicVersion` when matching rules change.
- Loading playlists starts a library-wide lookup pass (`ResolveAllYearsCommand`): every owned playlist, each unique track once, cached results skipped, progress and an estimate in `YearsStatus`, stoppable and resumable. A clicked track is looked up straight away; `MainViewModel._inFlightLookups` stops the pass and the click duplicating a lookup.
- MusicBrainz: ≤ 1 request/second (`MusicBrainzClient` enforces 1.1 s spacing, retries 503), User-Agent `SpotifyByYear/0.1 ( https://github.com/psandler/SpotifyByYear )`.
- Skip local files (`is_local`) and podcast episodes. They have no usable release year.

The work plan and decisions are in [documents/TODO.md](documents/TODO.md). Keep it current: check off finished items and add new ones as they come up.

## Tech stack

- .NET 10 (`net10.0`), C# with nullable enabled
- Avalonia 12.1.2 (Fluent theme), MVVM via CommunityToolkit.Mvvm 8.4.2
- SpotifyAPI-NET (`SpotifyAPI.Web` 7.4.2) for the Web API. 7.3.0+ supports the Feb 2026 API changes, so use the new `/items` methods, not the obsolete `/tracks` ones. The names are confusing: `GetItems`/`AddItems`/`RemoveItems`/`ReplaceItems`/`ReorderItems` are **obsolete** (old `/tracks` endpoints). Use `GetPlaylistItems`, `AddPlaylistItems`, `RemovePlaylistItems`, `ReplacePlaylistItems`, `UpdatePlaylistItems`. Treat CS0618 obsolete warnings on Spotify calls as bugs.
- Raw JSON: `SpotifyClient.LastResponse.Body` holds the raw response text. `SpotifyService` reads it under `_requestLock` so calls can't interleave, and `PlaylistItemParser` parses it so fields the library doesn't model are kept.
- Solution file is `SpotifyByYear.slnx` (the XML solution format). Single project: `SpotifyByYear/`.

## Commands

```bash
dotnet build SpotifyByYear.slnx
dotnet test --solution SpotifyByYear.slnx
dotnet run --project SpotifyByYear
```

- `global.json` opts `dotnet test` into Microsoft.Testing.Platform (required for xUnit v3 on the .NET 10 SDK), so use `--solution`/`--project`, not a positional path.
- If the app is running, builds fail because `SpotifyByYear.exe` is locked. Don't kill the user's app. Either ask them to close it, or build to another folder (`dotnet build SpotifyByYear.slnx --artifacts-path <scratch>`) and run the test exe from there (`<scratch>\bin\SpotifyByYear.Tests\debug\SpotifyByYear.Tests.exe`).
- Tests against real MusicBrainz/Spotify are skipped by default. To run them (PowerShell): `$env:SPOTIFYBYYEAR_LIVE_TESTS = "1"; dotnet test --solution SpotifyByYear.slnx`. The Spotify one needs a saved login from the app and never opens a browser.

## Layout and conventions

```
SpotifyByYear/
  Views/          .axaml windows/controls (+ minimal code-behind)
  ViewModels/     view models, derive from ViewModelBase
  Models/         plain data types
  Services/       Spotify client (ISpotifyService), sign-in, token storage, settings
  ViewLocator.cs  maps FooViewModel -> FooView by naming convention
SpotifyByYear.Tests/  xUnit v3 tests (Models/, Services/, ViewModels/, Live/, TestSupport/ fakes)
documents/        planning docs (TODO.md)
```

- MVVM: views hold no logic, and view models don't reference Avalonia controls.
- Observable properties use the partial-property form: `[ObservableProperty] public partial string Name { get; set; }`. Commands use `[RelayCommand]`.
- Use compiled bindings: set `x:DataType` on every view.
- Keep Spotify/HTTP code in services behind interfaces so the grouping logic can be unit tested without the network.
- Async all the way. Use no `.Result`/`.Wait()`. Pass `CancellationToken` through long operations.
- Tests: add or update tests in `SpotifyByYear.Tests` with every behavior change, and run `dotnet test` before saying work is done. Use the fakes in `TestSupport/` (`FakeSpotifyService`, `FakeMusicBrainzClient`) and `TempDirectory` for files. Tests must never open a browser or the app window, and must never touch the real `%LOCALAPPDATA%\SpotifyByYear` files (except the opt-in live Spotify test, which reads the saved login). App internals are visible to the test project via `InternalsVisibleTo`.

## Spotify API notes (Feb/Mar 2026 Development Mode changes)

- The app owner needs Spotify Premium, and Development Mode apps are limited to 5 users.
- Auth: Authorization Code with PKCE (no client secret). The redirect URI must be a loopback IP such as `http://127.0.0.1:5543/callback`. `localhost` is rejected.
- Playlist items: `GET/POST/PUT/DELETE /playlists/{id}/items`. The old `/tracks` endpoints were renamed.
- Playlist contents are only returned for playlists the user **owns or collaborates on**. Followed playlists return metadata only.
- Create playlists with `POST /me/playlists`. `POST /users/{id}/playlists` was removed.
- Batch catalog endpoints (`GET /tracks?ids=...`, `/albums`, `/artists`) were removed.
- Response fields renamed: `tracks` → `items`, `items[].track` → `items[].item`. `popularity`, `external_ids`, `available_markets`, and `linked_from` are gone.
- Page through results (playlist items: max 100 per page). Adding items takes at most 100 URIs per request.
- On HTTP 429, honor the `Retry-After` header.

## Secrets and local data

- Spotify Client ID: `SpotifyByYear/appsettings.Local.json` (git-ignored, copied to the build output). `appsettings.Local.example.json` is the committed template. It's not secret under PKCE, but it's personal, so keep it out of the repo.
- Token (incl. refresh token): `%LOCALAPPDATA%\SpotifyByYear\token.json` (`TokenStore`), never inside the repo. Delete it to force a fresh sign-in.
- Release-year cache: `%LOCALAPPDATA%\SpotifyByYear\release-years.json` (`ReleaseYearCache`, safe to delete). Manual corrections: `year-overrides.json` in the same folder, format `{ "overrides": { "<spotify track id>": { "year": 1977, "note": "..." } } }`. Never delete the overrides file.
- Don't launch the GUI app without asking the user first. Verify with `dotnet build` and `dotnet test`.
- OAuth callback: `LoopbackCallbackListener` (raw `TcpListener` on 127.0.0.1). Don't switch to `HttpListener`, which needs a URL ACL/admin rights to bind 127.0.0.1 on Windows.
