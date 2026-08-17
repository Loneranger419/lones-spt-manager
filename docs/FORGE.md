# How Lone's SPT Manager uses The Forge

For **The Forge** (`sp-mod.com`) maintainers.

**Lone's SPT Manager** is a local SPT 4.1 profile manager. Players search your catalogue, install mods, and check updates. Source: [github.com/Loneranger419/lones-spt-manager](https://github.com/Loneranger419/lones-spt-manager). The app is **not** listed on The Forge — GitHub Releases only — because a large part of it was written with AI tools ([your AI policy](https://sp-mod.com/content-guidelines)).

---

## API use

Public read-only API. No key, no account, no HTML scrape.

| | |
| --- | --- |
| Base | `https://sp-mod.com/api/v0/` |
| `User-Agent` | `Lones-SPT-Manager/<version>` |
| Methods | GET only |

Calls happen when the player searches, installs, or clicks **Updates**. Nothing polls you in the background.

| Action | Endpoints |
| --- | --- |
| Search | `GET /mods` (`per_page` ≤ 50) |
| Install | `GET /mod/{id}/versions`, `/mods/dependencies`, `/addons`, `/addons/dependencies`, `/mod/{id}` |
| Updates | `GET /mods/updates` |

Archives use the version `link` you return. They stay on the player's PC for their own SPT install. We do not rehost or upload them. Thumbnails come only from `sp-mod.com` / `files.sp-mod.com` and are cached locally. Same version is not downloaded again if we already have it.

---

## Your rules

- **User-Agent** is set on every request.
- **429 / `Retry-After`** is honoured (then backoff). We do not dodge the rate limit.
- **No scrape**, no login, no writes (no comments, ratings, or uploads).
- We do not submit this app as a Forge listing.

If you want a different User-Agent or fewer calls, open an issue on the repo and/or contact [Loneranger419](https://github.com/Loneranger419).
