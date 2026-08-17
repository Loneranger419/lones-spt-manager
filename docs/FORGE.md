# How Lone's SPT Manager uses The Forge

This note is for **The Forge** (`sp-mod.com`) maintainers. It describes what this Windows app calls, when it calls it, and how that lines up with your published API rules, Terms of Service, and Content Guidelines.

The app is **Lone's SPT Manager**. Source: [github.com/Loneranger419/lones-spt-manager](https://github.com/Loneranger419/lones-spt-manager). It is **not** listed on The Forge. It is **not** [SPT Mod Manager](https://sp-mod.com/mod/2851/spt-mod-manager).

Player how-to: [`README.md`](../README.md). Our own build notes: [`DEVELOPERS.md`](DEVELOPERS.md).

---

## What the app is

A local SPT 4.1 profile manager. Mods stay in `%AppData%\LonesSptManager` (or a folder the user picks). Deploy junctions them onto one bound SPT install. Players search The Forge, install, check updates, or apply a `mods.json` pack that names Forge mod ids.

The app never starts `EscapeFromTarkov.exe` or BattlEye. It does not touch live EFT.

---

## What we do not do

- We do **not** submit this app (or any AI-authored listing) to The Forge. Your [Content Guidelines](https://sp-mod.com/content-guidelines) AI policy is why. Distribution is GitHub Releases only.
- We do **not** scrape HTML, comments, or the website. No headless browser, no crawler.
- We do **not** create Forge accounts, send cookies, or use OAuth.
- We do **not** write to the API. No POST / PUT / DELETE. No ratings, comments, or uploads.
- We do **not** rehost or redistribute Forge archives. Downloads stay on the player's machine for their own SPT install.
- We do **not** call `forge.sp-tarkov.com` or `forge.sp-mod.com` (both dead / 525). Base URL is `https://sp-mod.com/api/v0/`.
- We do **not** download [SPT Mod Manager](https://sp-mod.com/mod/2851/spt-mod-manager) (id `2851`, slug `spt-mod-manager`). Search hides it; pack install skips it; a direct install is refused. Two managers on one install fight.
- We do **not** poll The Forge in the background. App self-update talks to **GitHub Releases**, not you.

---

## How we talk to you

Public, read-only API. No key. Same surface you document for “mod managers & browsers” on [The Forge API](https://sp-mod.com/developers).

| Item | Value |
| --- | --- |
| Origin | `https://sp-mod.com` |
| API | `https://sp-mod.com/api/v0/` |
| Docs we follow | [API reference](https://sp-mod.com/docs/index.html), [developers](https://sp-mod.com/developers), [Terms](https://sp-mod.com/terms-of-service) |
| `User-Agent` | `Lones-SPT-Manager/<version>` (today `Lones-SPT-Manager/0.1.5`) |
| Auth | None |
| HTML scrape | None |

Implementation: `src/Lones.SptManager.Forge/ForgeClient.cs`.

### Endpoints (GET only)

Called when the **player** searches, installs, or clicks **Updates**. Not on a timer.

| When | Path |
| --- | --- |
| Search (The Forge tab) | `GET /mods?query=…&include=versions&per_page≤50&filter[spt_version]=^4.1.2&sort=-updated_at` |
| Install | `GET /mod/{id}/versions`, `GET /mods/dependencies`, `GET /addons?filter[mod_id]=…`, `GET /addons/dependencies`, `GET /mod/{id}` (name + thumbnail) |
| **Updates** button | `GET /mods/updates?mods=id:version,…&spt_version=4.1.2` |
| Missing list art (once per missing tile) | `GET /mod/{id}` then the thumbnail URL |
| Optional connectivity | `GET /ping` |

We do **not** call `…/file-tree`. Install uses the version `link` and our own archive mapper.

A **pack** (`mods.json`) is a file the player points at (HTTPS or local). That fetch is not your API. Each listed `id` + `installedVersion` then goes through the same install path as the Forge tab. Pack “is there an update?” only re-reads that JSON; it does not hammer `/mods/updates`.

### Downloads

The archive URL is the `link` on the version (or dependency / addon version) you returned. We GET that URL, write `%AppData%\LonesSptManager\cache\forge\`, extract into an immutable **store**, then deploy from the store. Re-install of the same version reuses the store when we already have it.

Thumbnails GET only `files.sp-mod.com` or `sp-mod.com` and cache under `cache/thumbnails/`. Other hosts are dropped.

`conflict: true` on a dependency node aborts that install (your AC-C2). `fika_compatibility=incompatible` is a warning, not a silent install.

---

## How this matches your usage rules

From [Be a good citizen](https://sp-mod.com/developers) and the [rate-limit section](https://sp-mod.com/docs/index.html):

**Descriptive User-Agent.** Every Forge HTTP client sets `Lones-SPT-Manager/<version>` so you can find us.

**Honour 429 and `Retry-After`.** `SendWithRetryAsync` treats `429` and `503` as retryable, waits `Retry-After` (or exponential backoff, cap 2 minutes), up to 8 attempts. We do not rotate IPs, strip the User-Agent, or otherwise dodge the edge limit. Your docs say those numbers may change; we key off the status and header, not a hardcoded 40/10s or 200/60s.

**Cache and do not poll harder than needed.**

- Search / install / Updates run because the player clicked.
- Archives and thumbnails stay on disk; a second install of the same version does not download again if the store already has it.
- Thumbnail backfill for old store rows is one `GET /mod/{id}` per missing Forge id, with **250 ms** between those calls, and only if name or art is missing.
- Search `per_page` is clamped to 50.

**Terms of Service — technical restrictions.**

- No HTML scraping (crawlers, scrapers, headless browsers). Catalogue data is JSON from `/api/v0`.
- No login, no bypass of access controls, no reverse engineering of the site.
- Bandwidth is player-driven install/search, plus small thumbnails. We do not mirror the catalogue.

**Terms of Service — acceptable use.** Downloads are for that player's SPT install (your “download and use mods for personal SPT gameplay”). We do not upload those zips anywhere, including back to The Forge.

**Content Guidelines — AI.** A large part of this app was written with AI coding tools. We treat that as a reason **not** to list the manager on The Forge. We are not asking you to host the exe. Players who care can read the GitHub source.

**Content Guidelines — we are not a mod submission.** We do not upload SPT client/server mods. We only consume listings authors already published.

---

## Local data (not sent to you)

Manager data is on the player's PC: store, profiles, Overwrite, BepInEx config junctions, Forge zip cache, thumbnail cache. We do not send SPT profiles, `credentials.json`, or `server.key` to The Forge or to us.

---

## If something looks wrong

Open an issue on the repo, or contact the maintainer via GitHub ([Loneranger419](https://github.com/Loneranger419/lones-spt-manager)). If you need a different User-Agent, a lower request pattern, or an endpoint dropped, say so — those are small changes on our side.
