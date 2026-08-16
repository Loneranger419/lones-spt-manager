# Lone's SPT Manager

A Windows mod manager for [SPT](https://sp-tarkov.com/) (Single Player Tarkov) 4.1, in the spirit of Mod Organizer 2: mods live **outside** the game folder, each **profile** keeps its own enabled set / saves / configs / generated files, and [The Forge](https://sp-mod.com/) is the catalogue.

Source: [github.com/Loneranger419/lones-spt-manager](https://github.com/Loneranger419/lones-spt-manager).

WPF app, `net10.0-windows` (`Lones.SptManager.slnx`). Built against SPT **4.1.2**.

Not affiliated with SPT, SP-Tushonka, Battlestate Games, or Mod Organizer 2.

---

## Run it

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) (see `global.json`).

```
dotnet test Lones.SptManager.slnx
dotnet run --project src/Lones.SptManager.App
```

On first launch: **Bind** the SPT game root (the folder with `EscapeFromTarkov.exe`, `BepInEx`, and `SPT_Runtime`). Manager data defaults to `%AppData%\LonesSptManager`.

---

## How it works

Mods are extracted into an immutable **store**. Each profile gets a **staging** tree (the merge the game may write into). **Deploy** junctions install folders onto that staging tree — never onto the store. Junctions write through, so pointing the live install at the shared store would mutate it.

After you quit, **Harvest** copies new or changed files into **Overwrite**. You can assign those to a mod (new store version; the hashed original stays put) or discard them.

`BepInEx\plugins` itself stays a real folder because SPT-owned `spt\` lives there. Deploy junctions each extra plugin **subdirectory**. Loose `BepInEx/plugins/*.dll` packages are wrapped into `plugins/<name>/` first. Check `dir /AL BepInEx\plugins` for `<JUNCTION>` — files inside a junction look normal in Explorer.

**Load order** on the left list is file overlay: **0 at the top loads first**, later number wins. That is not SPT’s server ModGuid order. An empty saved enabled list means all mods off.

---

## Using the app

- **Profiles** — dropdown switches and deploys. Add can copy from another profile (saves, generated files, BepInEx configs, Overwrite, enabled mods) or install a Forge **pack** from an HTTPS / local `mods.json` (`id` + `installedVersion`; list order = load order 0 first). Edit can rename, copy, or delete (not the last profile).
- **Packs** — progress popup with download size and extract `N / M` files plus per-file bytes. Cancel aborts the current download or extract. Store hits are reused. Failed entries are skipped. Zips use `System.IO.Compression` (zip64); 7z uses SharpCompress.
- **The Forge** — search `sp-mod.com` (no token). Install downloads to `cache/forge/` then the store. `conflict: true` blocks. `fika_compatibility=incompatible` warns. Honour HTTP 429 / `Retry-After`.
- **Launch** — **solo** / **Fika host** starts `SPT_Runtime\SPT.Server.exe` then `SPT.Launcher.exe` (cwd `SPT_Runtime`), waits for TCP 6969 or log `Server has started`. **Fika join** starts the launcher only and writes `user\launcher\config.json` `Url` without dropping other keys. Never starts `EscapeFromTarkov.exe` or BattlEye. Harvest after you quit.
- **Leftovers** — right-click **Import leftover** to claim a real install folder into the store, then Deploy to junction it.
- **Purge manager data** — detaches junctions and wipes store / profiles / instances / cache. Does **not** delete the SPT install. Bind afterward.

Theme follows Windows **Settings → Personalization → Colors**. Default window is 1330×930. Forge thumbnails cache under `cache/thumbnails/`.

---

## SPT 4.1 layout

Game root has `EscapeFromTarkov.exe`, `BepInEx`, Doorstop (`winhttp.dll`). Server, launcher, and `user` live under **`SPT_Runtime`**, not the 4.0 wiki’s `SPT\` folder.

| Kind | Path |
| --- | --- |
| Server mods | `SPT_Runtime\user\mods` |
| Client mods | `BepInEx\plugins` |
| Saves | `SPT_Runtime\user\profiles` |
| F12 configs | `BepInEx\config` |
| Prepatchers | `SPT_Runtime\user\patchers` |
| Fika join URL | `SPT_Runtime\user\launcher\config.json` (missing on stock 4.1.2 until something writes it) |

Do not delete `BepInEx\plugins\spt` or `BepInEx\patchers\spt-prepatch.dll`. 4.1.2 does not run 4.0.X mods. Archives may use `SPT_Runtime/`, wiki `SPT/`, bare `user/`, a wrapper folder, or backslash zip paths — the mapper normalizes those. Root `.exe` tools are not merged into the game tree by default.

**Fika host:** SPT.Server then SPT.Launcher (TCP 6969; UDP 25565 is the raid-hosting **client**). **Fika join:** launcher only, URL `https://host:6969`. Joiners still need the Fika client plugin. Plugin zip: `BepInEx/plugins/Fika/`. Server zip: `SPT_Runtime/user/mods/fika-server/`.

Forge API is `https://sp-mod.com/api/v0` (public, read-only). Do not use `forge.sp-tarkov.com` or `forge.sp-mod.com` (both dead). File-tree listings are optional; install still works without one.

---

## Solution

| Project | Role |
| --- | --- |
| `src/Lones.SptManager.App` | WPF UI |
| `src/Lones.SptManager.Core` | Bind, mapper, store, deploy, profiles, harvest, launch |
| `src/Lones.SptManager.Forge` | Forge HTTP + pack install |
| `src/Lones.SptManager.Native` | Junctions, hardlinks, volume IDs, no-follow deletes |
| `tests/Lones.SptManager.Tests` | Bind / mapper / deploy / profile / Forge / launch |

Locked product choices: **C# / WPF**; Forge-native on `sp-mod.com`; profiles + Overwrite; mods off-install; Fika client-only launch; **not USVFS**. Target is `net10.0-windows`.

Privacy: never dump profile JSON, `credentials.json`, or `server.key` contents.

`.gitignore` drops `bin/` `obj/` `.vs/` user files, NuGet junk, secrets, local SPT/manager runtime, `PLAN.md`, and `research/`.
