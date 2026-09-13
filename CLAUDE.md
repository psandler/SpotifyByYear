# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Hard rules

- **No git write operations of any kind.** The human does every git write: `init`, `add`, `commit`, `branch`, `checkout`/`switch`, `merge`, `rebase`, `cherry-pick`, `stash`, `tag`, `reset`, `restore`, `rm`, `mv`, `push`, `pull`, `fetch`, `config`, and anything else that changes the repo, index, refs, or remotes. Read-only commands (`status`, `diff`, `log`, `show`, `blame`) are fine. When work is ready to commit, say so and suggest a commit message. Don't run the commit.
- **No GitHub access.** Don't use the `gh` CLI or GitHub API/MCP tools. The human handles everything on GitHub (repo, PRs, issues).
- **Never commit or print secrets.** The Spotify Client ID lives in local config (see below), and tokens live in the user profile. Neither belongs in source.

## Project

SpotifyByYear is a desktop app that reads the user's own Spotify playlists and builds new playlists grouped by **track release year**.

- Sources: all playlists the **user owns**. More sources and features are TBD; the user will explain them as we go.
- "Year" means the song's **original** release year. **Unresolved:** Spotify only provides `album.release_date` (formats `YYYY`, `YYYY-MM`, `YYYY-MM-DD`; see `release_date_precision`), which is the date of the album this copy is on. It's wrong for compilations and remasters (e.g. "Rich Girl" → 2010 instead of 1977). Don't build grouping logic on it until the approach in documents/TODO.md is decided.
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
dotnet run --project SpotifyByYear
```

## Layout and conventions

```
SpotifyByYear/
  Views/          .axaml windows/controls (+ minimal code-behind)
  ViewModels/     view models, derive from ViewModelBase
  Models/         plain data types
  Services/       Spotify client (ISpotifyService), sign-in, token storage, settings
  ViewLocator.cs  maps FooViewModel -> FooView by naming convention
documents/        planning docs (TODO.md)
```

- MVVM: views hold no logic, and view models don't reference Avalonia controls.
- Observable properties use the partial-property form: `[ObservableProperty] public partial string Name { get; set; }`. Commands use `[RelayCommand]`.
- Use compiled bindings: set `x:DataType` on every view.
- Keep Spotify/HTTP code in services behind interfaces so the grouping logic can be unit tested without the network.
- Async all the way. Use no `.Result`/`.Wait()`. Pass `CancellationToken` through long operations.

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
- OAuth callback: `LoopbackCallbackListener` (raw `TcpListener` on 127.0.0.1). Don't switch to `HttpListener`, which needs a URL ACL/admin rights to bind 127.0.0.1 on Windows.
