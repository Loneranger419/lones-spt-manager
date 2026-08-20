<p align="center">
  <img src="assets/lones-spt-manager-card.png" alt="Lone's SPT Manager">
</p>

# Lone's SPT Manager

A Windows manager for **SPT 4.1**. Mods stay **outside** the game folder. Each **profile** keeps its own enabled mods, load order, saves, configs, and leftover files. [The Forge](https://sp-mod.com/) is built in for search and install.

Download the exe from [GitHub Releases](https://github.com/Loneranger419/lones-spt-manager/releases/latest/download/LonesSptManager.exe). This app is not listed on The Forge.

**Note to Forge staff:** how this app uses your API is in [`docs/FORGE.md`](docs/FORGE.md).

Use it if you want to:

- Keep a clean SPT install and swap setups without copying folders by hand
- Run more than one profile (solo vs Fika, different packs, a test character)
- Install from The Forge or from a shared `mods.json` pack
- Launch **solo**, **Fika host**, or **Fika join** from one window

This is **not** [SPT Mod Manager](https://sp-mod.com/mod/2851/spt-mod-manager). Do not run both against the same install. This app will not download SPT Mod Manager from The Forge.

Not affiliated with SPT, SP-Tushonka, Battlestate Games, or Mod Organizer 2. [MIT](LICENSE) licensed.

AI coding tools wrote a large part of this app. That is why it is not on The Forge ([their AI policy](https://sp-mod.com/content-guidelines)). Read the source if you care how it works before you point it at your SPT install.

<p align="center">
  <img src="src/Lones.SptManager.App/Assets/readme-main.png" alt="Main window with a profile, installed mods, Deploy and Harvest, and The Forge tab">
</p>

---

## Install

1. Install a working **SPT 4.1.2** copy first (the folder with `EscapeFromTarkov.exe`, `BepInEx`, and `SPT_Runtime`).
2. Download the latest **zip** from [GitHub Releases](https://github.com/Loneranger419/lones-spt-manager/releases).
3. Extract it somewhere **that is not** your SPT game folder. Put `LonesSptManager.exe` on the desktop or in its own folder.
4. Run `LonesSptManager.exe`. No extra .NET install is required.
5. If Windows says **Windows protected your PC**, that is common for an unsigned exe. Use **More info → Run anyway** only if you trust the download. [VirusTotal report for V0.1.7](https://www.virustotal.com/gui/file/f068c4abd92cb8af9823272e725a5f7d1758ff6de9f1ba12aeb07783474c3444).
6. **Bind** the SPT game root (the same folder as `EscapeFromTarkov.exe`). Manager data defaults to `%AppData%\LonesSptManager`.
7. Add a **profile** (or keep the first one) and start installing mods.

`mods.json.example` in the zip is a sample pack file. You do not need it unless you are building or sharing a pack.

### Remove it

1. In the app, **Settings → Purge manager data** if you want the junctions and cached mods gone. That does **not** delete your SPT install, and it will not delete `LonesSptManager.exe` (or `mods.json.example`) if those files sit in the manager data folder.
2. Close the app and delete `LonesSptManager.exe` (and the folder you extracted).
3. Optional: delete `%AppData%\LonesSptManager`.

After a purge, **Bind** again if you still want to use the manager.

---

## How to use

### Daily loop

1. Pick a **profile** at the top. Switching deploys that profile onto the game.
2. Enable or disable mods on the left list. Drag to change **load order** (0 at the top loads first; later rows win when files overlap).
3. **Deploy** (under the installed list) if you changed mods and are not about to launch. **Un-Deploy** only unlinks the install (junctions and copied leftovers). It does not Harvest — configs already live in the profile through those links. Store and profiles stay. Closing the app does the same by default; turn that off in **Settings**.
4. **Solo**, **Fika host**, or **Fika join**. Launch deploys the current profile first, then starts the server (solo / Fika host) and waits until the log says it has started, then opens the launcher. The spinner says which of those it is waiting on, then stays up until that session’s server and/or client have quit, then **Harvest** runs on its own. The server console is started so it does not sit waiting for Enter (inherited stdin / Quick Edit).
5. Use **Harvest** on the installed list if you played without launching from this app.

The window blurs with a spinner during profile switch, Deploy, Un-Deploy, Harvest, and Launch. Wait it out.

### Install mods

- **The Forge** tab — search, select, **Install**. **Updates** checks what you already have. **Import zip** takes a `.zip` / `.7z` you already downloaded.
- **Add profile → Install from pack** — HTTPS link or a local `mods.json`. List order is load order (0 first). Each entry needs a Forge `id` and `installedVersion`. Forge addons use `"kind": "addon"` with the addon id (not the parent mod id). Failed mods are skipped. Edit the profile later and **Update** to reinstall that pack.

A pack can copy from another profile **or** install from JSON, not both in one go.

### Profiles

**Add** can start empty, copy from another profile (saves, generated files, BepInEx configs, Overwrite, enabled mods), or install a pack.

**Edit** can rename, copy, delete (not the last profile), or **Update** a saved pack link.

When a profile has a pack link, loading it checks that `mods.json` for newer versions or new mods. A **Pack update** button appears next to Edit if something changed. It does not install anything until you click **Edit → Update**. Mods you added yourself stay on the profile; a pack update only takes them over if that pack starts listing them.

The last profile you used is selected again next time.

### Overwrite

After Harvest, extra files land on the **Overwrite** tab. Configs that belong to a mod are attached to that mod for this profile — including F12 BepInEx `.cfg` files matched by plugin folder or DLL name when the Forge GUID is missing. Greyer rows are generated/state files that stay in Overwrite. **Assign to mod** pins a leftover onto the selected installed mod for this profile (same as Harvest; it does not create a second store entry). You can also **Discard file** or **Discard all Overwrite**.

### Already-modded install

If SPT already has real folders in `BepInEx\plugins` or `SPT_Runtime\user\mods`, right-click a leftover row → **Import leftover into store**, then Deploy so the manager owns it.

### Other buttons

- **Settings** — theme (follow Windows, Dark, or Light), manager data folder, **Purge manager data**, **Repair** (stuck or half-applied deploy), **Remove junctions when the app closes**, and **Check for updates**.
- **App update** — appears in the header when a newer GitHub Release exists. It downloads the zip, replaces `LonesSptManager.exe`, and restarts. Manager data and the SPT install stay put.
- **Bind** — next to the game root. Point the manager at this SPT install.

---

## Fika

Install the Fika **client** and **server** mods on the profile the same way as any other Forge mods. Everyone in the group needs the Fika client plugin.

| Button | What it starts | When to use |
| --- | --- | --- |
| **Solo** | SPT server, then the SPT launcher | Normal single-player |
| **Fika host** | SPT server, then the SPT launcher | You are hosting |
| **Fika join** | SPT launcher only | You are joining someone else |

For **Fika join**, put the host URL in **Join URL** (example: `https://their-pc:6969`). The manager writes that into the launcher config and leaves the other keys alone.

This app never starts `EscapeFromTarkov.exe` or BattlEye. You click Play in the SPT launcher like usual.

---

## FAQ

**Do I extract this into the SPT folder?**
No. Keep the exe outside the game. Bind the game root from inside the app.

**Can I use this with [SPT Mod Manager](https://sp-mod.com/mod/2851/spt-mod-manager)?**
No. Pick one manager for an install.

**Where did my F12 / mod settings go?**
Quit the game. If you launched from this app, Harvest runs when SPT quits. Otherwise click **Harvest** under the installed list. Settings that belong to a mod stay with that mod on this profile.

**ReShade asks me to do the tutorial every launch.**
ReShade could not write `ReShade.ini` (often after a pack copy), so it saved to `ReShade2.ini`. Deploy was restoring the pack `ReShade.ini` and ignoring that sidecar. Deploy/Harvest now copy the finished tutorial back into `ReShade.ini` and clear the read-only flag.

**F12 does nothing but other plugins work.**
That menu is Configuration Manager (it ships with SPT). An empty profile `BepInEx\config` gets BepInEx defaults: hotkey **F1**, and `HideManagerGameObject = false`. EFT can then kill the menu object so **no F key** opens it, while Harmony mods still work. Deploy now seeds F12 and sets `HideManagerGameObject = true`. Quit and relaunch after that change.

**I changed mods but the game looks the same.**
**Deploy** before you play if you changed mods. Make sure the checkboxes on the left are what you want. An empty enabled list means everything is off.

**I disabled a server mod but SPT still tries to load it.**
**Deploy** after you uncheck it. Leftover files for that mod (Overwrite / empty folders under `SPT_Runtime\user\mods`) are skipped or removed on deploy so SPT does not see a hollow folder.

**A Forge / pack install failed.**
Read the Log tab. Other mods still install. Try again, or install that one from **The Forge** tab.

**Windows blocked the exe.**
Unsigned indie builds often trip SmartScreen. Use **More info → Run anyway** only if you got the file from the GitHub Release. Each release has a [VirusTotal report](https://www.virustotal.com/gui/file/f068c4abd92cb8af9823272e725a5f7d1758ff6de9f1ba12aeb07783474c3444) (V0.1.7).

**Does Purge delete Tarkov / SPT?**
No. It only removes this manager’s data (store, profiles, cache, settings). Bind again afterward. If you keep `LonesSptManager.exe` in that same folder, Purge leaves the exe there.

**Linux / Steam Deck?**
Windows only.

**How do I switch dark mode?**
**Settings** (top right). Theme can follow Windows, or stay Dark / Light. The manager data folder, **Purge manager data**, **Repair**, **Remove junctions when the app closes**, and **Check for updates** are in the same window. Unchecked mods in the list are greyed out.

**I closed the app and SPT looks vanilla.**
That is **Un-Deploy**. Closing unlinks the install by default (same as the button). **Deploy** or **Solo** / **Fika host** / **Fika join** puts the profile back. Uncheck **Remove junctions when the app closes** in Settings if you want the install to stay deployed after quit. If SPT (server, launcher, or the game) is still running, Un-Deploy and close will tell you to quit those first.

**Will the app update itself?**
On launch it asks GitHub if a newer Release exists. **App update** (or **Settings → Install update**) downloads that zip, replaces the exe, and restarts. It does not touch manager data or the SPT folder. **Settings → Check for updates** only checks. If the folder is not writable, open the GitHub Release and replace the exe yourself.

---

Build from source and internals: [`docs/DEVELOPERS.md`](docs/DEVELOPERS.md).
