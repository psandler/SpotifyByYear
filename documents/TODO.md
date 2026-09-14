# SpotifyByYear — TODO

Goal: a desktop app (Avalonia) that reads my Spotify playlists and builds new playlists grouped by year.

## Project setup

- [x] Create `documents/` folder and this todo list
- [x] Create .NET solution (`SpotifyByYear.slnx`)
- [x] Create Avalonia UI project (`SpotifyByYear`, MVVM with CommunityToolkit, .NET 10, Avalonia 12.1.2) and add it to the solution
- [x] Verify the project builds and the empty window runs
- [x] **(Human)** Initialize git repo (`git init`)
- [x] Add `.claude/settings.json` deny rules (git writes, `gh`, GitHub MCP)
- [x] Add `.gitignore` (standard .NET, plus local secrets/token files)
- [x] Add `.editorconfig` (default `dotnet new editorconfig` style)
- [x] Create `CLAUDE.md` (project overview, build/run commands, conventions, Spotify API gotchas, no-git-writes rule)
- [ ] Add `README.md` (what it is, how to set up a Spotify app, how to run)
- [ ] **(Human)** Initial commit
- [ ] **(Human)** Create GitHub repo and push
- [ ] (Optional) Add a test project (`SpotifyByYear.Tests`, xUnit) for the year-grouping logic
- [ ] (Optional) Connect Claude Code to Visual Studio 2026 (community extension, see notes below)

## Spotify developer setup

- [x] Confirm the Spotify account that owns the app has Premium (required for Development Mode apps since Feb 2026)
- [x] **(Human)** Register an app in the Spotify Developer Dashboard (API: Web API) and get the Client ID
- [ ] **(Human)** Add the redirect URI `http://127.0.0.1:5543/callback` to the app (`localhost` is not allowed)
- [x] Create `SpotifyByYear/appsettings.Local.json` with the Client ID (git-ignored)
- [x] Client ID supplied via `SpotifyByYear/appsettings.Local.json` (git-ignored, copied to the build output)
- [x] Token (incl. refresh token) stored at `%LOCALAPPDATA%\SpotifyByYear\token.json`

## Technical decisions

- [x] Auth flow: Authorization Code with PKCE (desktop app, no client secret needed)
- [x] Spotify client: **SpotifyAPI-NET** (`SpotifyAPI.Web` 7.4.2). Feb 2026 support arrived in 7.3.0, and 7.4.x added `ReplacePlaylistItems`.
- [x] Added `SpotifyAPI.Web`. The OAuth callback uses our own small `TcpListener` on 127.0.0.1 (`LoopbackCallbackListener`), not `SpotifyAPI.Web.Auth` or `HttpListener` (which needs admin rights for 127.0.0.1 on Windows)
- [x] Scopes requested: playlist read private/collaborative + modify private/public (so creating playlists later won't need a second sign-in)
- [x] "Year" = **track release year** (album `release_date`, first 4 chars). More detail to come with features.
- [x] **DECIDED:** MusicBrainz (ISRC, then search) first; Spotify search on every track as a second opinion and fallback; album year as an upper bound; manual overrides win. Results cached in `%LOCALAPPDATA%\SpotifyByYear\release-years.json` (JSON), overrides in `year-overrides.json`. See CLAUDE.md.
- [x] Explorer shows resolved year + source, MusicBrainz result, Spotify search result, candidates, "Look up again"
- [ ] Verify matching quality on real playlists; tune `TrackMatching` rules (bump `LogicVersion`)
  - First headless sample (11 tracks): all plausible; MusicBrainz and Spotify search agreed on every track where both answered
  - Library stats: 899 unique tracks, 172 on compilations, 173 with version text, 0 without ISRC
  - Cold lookups took ~6.5 s/track in the sample (one included 503 retries), so a full first run could be 1+ hour. Needs a background pass that can resume.
  - [ ] **Decide:** re-recordings like "All Too Well (Taylor's Version)" currently resolve to the *original* song's year (2012), not the re-recording (2021)
  - [ ] **Decide:** live versions resolve to the studio original's year (e.g. "Valerie - Live At BBC Radio 1…")
- [x] MusicBrainz 503 handling: longer backoff (5/10/15 s or `Retry-After`) that pauses all MusicBrainz requests
- [ ] UI for setting a manual year override (for now: hand-edit `year-overrides.json`)
- [ ] Resolve years for all selected playlists in the background (not just the one being viewed)
- [x] ~~OPEN — album date ≠ original release date.~~ Background: Spotify has no original-release field; `album.release_date` is the date of the album this copy is on. Compilations/remasters give the wrong year (e.g. "Rich Girl" shows 2010 from *70s 100 Hits*; the song is from 1977). The ISRC year code isn't it either (`USRC19206280` → 1992 registration). Options:
  - Spotify search for the same title + artist, take the earliest non-compilation album date (search limit is 10 per page in Dev Mode; one search per song)
  - MusicBrainz lookup by ISRC (`first-release-date`), falling back to title + artist search (free, no key, 1 request/sec, needs a User-Agent)
  - Discogs API (needs a token, 60 requests/min)
  - Cache results locally so each song is looked up once; allow manual overrides
- [x] Sources: **all playlists I own**. Other features TBD.
- [ ] Naming convention for generated playlists (e.g. `2019 — By Year`)
- [ ] Re-run behavior: update existing year playlists, skip duplicates, or recreate from scratch

## Features (first pass)

- [x] Sign in with Spotify (browser opens, app catches the callback, tokens saved). Code done; **needs a real test once the Client ID is set up**
- [x] List my playlists in the UI (owned only, alphabetical, checkboxes, tri-state "Select all", "N of M selected")
- [x] Sign-in verified with a real account
- [x] API explorer: click a playlist → its tracks (all pages, cached per playlist); click a track → key fields + full raw JSON of the playlist item
- [ ] Auto-connect on startup when a saved token exists
- [ ] Keep checkbox selections when reloading the list
- [ ] Load tracks from the selected playlists (handle paging)
- [ ] Group tracks by year and show a preview (year → track count)
- [ ] Create or update the year playlists (batch adds of 100 items per request)
- [ ] Progress reporting and rate-limit handling (HTTP 429 `Retry-After`)
- [ ] Error handling and sign-out

## Spotify API notes (as of Feb/Mar 2026 changes)

- Development Mode apps: the owner needs Premium; limited to 5 users.
- Playlist items: `GET/POST/PUT/DELETE /playlists/{id}/items` (the old `/tracks` endpoints were renamed).
- Playlist contents are only returned for playlists the user **owns or collaborates on**. Followed playlists return metadata only.
- Create playlists with `POST /me/playlists` (`POST /users/{id}/playlists` was removed).
- Batch catalog endpoints (`GET /tracks?ids=...`) were removed.
- Response field renamed: `tracks` → `items`, `items[].track` → `items[].item`.

## Working agreement

- Claude does **no git write operations** and **no GitHub/`gh` access**. The human does all of it (see `CLAUDE.md`).

## Visual Studio 2026 + Claude Code

Official Claude Code IDE integrations exist only for VS Code and JetBrains (Rider). For Visual Studio there are community-built, unofficial extensions:

- "Claude Code for Visual Studio" (firish/claude_code_vs): implements Claude Code's IDE protocol, so edits show in VS's diff viewer and Claude can see selection and build errors.
- "Claude Code for Visual Studio" (nachum-shmilovitz-66): a chat tool window that drives the `claude` CLI.
- "Claude Code Extension" (dliedke): a multi-agent front end.

Alternatives: keep using this desktop app side by side with VS (both see the same files), or open the folder in Rider/VS Code for the official integration.
