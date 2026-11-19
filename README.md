<div align="center">

# UltimateMenu

**A Gorilla Tag mod menu with a Liquid Glass interface & Shaders**

**v1.8 series** · **18 languages**

<a href="https://store.steampowered.com/app/250820/SteamVR"><img src="https://www.google.com/s2/favicons?domain=store.steampowered.com&amp;sz=64" width="64" alt="SteamVR"></a> <a href="https://www.meta.com/fr/quest/setup"><img src="https://www.google.com/s2/favicons?domain=meta.com&amp;sz=64" width="64" alt="Meta Horizon Link"></a>

<p>
  <a href="https://www.nexusmods.com/gorillatag/mods/1330"><img src="https://www.google.com/s2/favicons?domain=nexusmods.com&amp;sz=64" width="32" alt="Nexus Mods"></a>
  &nbsp;
  <a href="https://gamebanana.com/mods/716098"><img src="https://www.google.com/s2/favicons?domain=gamebanana.com&amp;sz=64" width="32" alt="GameBanana"></a>
  &nbsp;
  <a href="https://github.com/MATTRAX64/UltimateMenu/releases/"><img src="https://github.githubassets.com/favicons/favicon.png" width="32" alt="GitHub Releases"></a>
  &nbsp;
  <a href="https://discord.com/invite/bucBZEdxce"><img src="https://cdn.discordapp.com/icons/810644499763691540/97ccd8637aa79c7970cd6f58999766a5.png" width="32" alt="Gorilla Tag Modding Discord"></a>
  &nbsp;
  <a href="https://www.patreon.com/c/MATTRAXER64xp"><img src="https://www.patreon.com/favicon.ico" width="32" alt="Patreon"></a>
</p>

[![Release](https://img.shields.io/github/v/release/MATTRAX64/UltimateMenu?style=flat-square&color=6c5ce7)](https://github.com/MATTRAX64/UltimateMenu/releases)
[![Downloads](https://img.shields.io/badge/dynamic/json?url=https://raw.githubusercontent.com/MATTRAX64/UltimateMenu/main/scripts/downloads.json&query=$.total_affiche&label=downloads&color=6c5ce7&style=flat-square)](https://github.com/MATTRAX64/UltimateMenu/releases)

[Download](https://github.com/MATTRAX64/UltimateMenu/releases) · [Report a bug](https://github.com/MATTRAX64/UltimateMenu/issues) · [Contact](https://mattraxer64xp.page.gd)

![UltimateMenu interface preview](Thumbnail/Menu.png)

![URP visual effects preview](Thumbnail/PostVolumeURP.png)

</div>

> **UltimateMenu is an independent community project. This project uses AI to help compress and optimize files before uploading them to GitHub.**

## <img src="UltimateMenu/Assets/Menu/Icons/Sun.png" width="22" alt=""> What's new in v1.8

The **Liquid Glass** interface uses URP post-processing to render glass effects in the menu.

## <img src="UltimateMenu/Assets/Menu/Icons/Mods.png" width="22" alt=""> Features

| | Feature | What you can do |
| :---: | :--- | :--- |
| <img src="UltimateMenu/Assets/Menu/Icons/Mods/VisualEffects.png" width="24" alt=""> | **Visual effects** | Customize the look of the game with visual presets and post-processing. |
| <img src="UltimateMenu/Assets/Menu/Icons/Mods/Camera.png" width="24" alt=""> | **Character and camera** | Adjust character interactions and camera controls. |
| <img src="UltimateMenu/Assets/Menu/Icons/Mods/MotionCapture.png" width="24" alt=""> | **Motion capture** | Export recordings and import them into Blender. |
| <img src="UltimateMenu/Assets/Menu/Icons/Settings.png" width="24" alt=""> | **Profiles** | Import and export settings and customize the menu’s appearance, language, and sound. |
| <img src="UltimateMenu/Assets/Menu/Icons/Mods/Performance.png" width="24" alt=""> | **Performance** | Adjust Unity performance settings. |
| <img src="UltimateMenu/Assets/Menu/Icons/Mods/Discord.png" width="24" alt=""> | **Discord Rich Presence** | Display your in-game activity on Discord. |

Each mod has its own settings.

## <img src="UltimateMenu/Assets/Menu/Icons/Download.png" width="22" alt=""> Installation

1. Install [BepInEx for Gorilla Tag](https://github.com/MATTRAX64/GorillaTagModInstaller).
2. Download `UltimateMenu.dll` from [GitHub Releases](https://github.com/MATTRAX64/UltimateMenu/releases).
3. Place the DLL in your game’s `BepInEx\plugins` folder.
   - **Steam** — `C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag\BepInEx\plugins`
   - **Meta PC (Oculus)** — `C:\Program Files\Oculus\Software\Software\another-axiom-gorilla-tag\BepInEx\plugins`
4. Launch Gorilla Tag.

## <img src="UltimateMenu/Assets/Menu/Icons/VR.png" width="22" alt=""> Usage

A **required tutorial** shows you how to open the menu in VR. On desktop, the menu hotkey appears when the mod loads.

### Blender and 3D objects

- **Mocap** — Install the [GorillaMocap Blender add-on](UltimateMenu/Assets/GorillaMocap/GorillaMocap-BlenderAsset.zip), then open it from the 3D Viewport or press **N** to find it in the sidebar.
- **3D objects** — Use [AutoBundle.unitypackage](UltimateMenu/Assets/GorillaSpawners/AutoBundle.unitypackage) to create your own bundles with the Unity version used by Gorilla Tag. You can also browse the [community objects](GorillaSpawners_3D_Objects/).

## <img src="UltimateMenu/Assets/Menu/Icons/Folder.png" width="22" alt=""> Files and folders

UltimateMenu stores its data under `BepInEx/plugins/UltimateMenu/`.

| File or folder | Contents |
| :--- | :--- |
| `Profile.json` | Menu settings |
| `GorillaFaces/` | Custom face images |
| `GorillaMocap/` | Mocap exports for Blender |
| `GorillaSpawners/` | 3D object `.bundle` files |
| `GorillaStreams/` | Text for OBS, including the room code and player count |
| `CrashReports/` | Error reports when reporting is enabled |

## 🔐 Privacy

- **Error reporting** is enabled by default to help diagnose and fix issues. You can turn it off in the mod menu settings.
- **Update checks** use the GitHub API at startup.

## ⚠️ Warning

Using mods may result in a ban under the game’s rules. Use UltimateMenu responsibly and at your own risk.

---

<div align="center">

**MATTRAX** · [Contact](https://mattraxer64xp.page.gd) · [Patreon](https://www.patreon.com/c/MATTRAXER64xp)

</div>
