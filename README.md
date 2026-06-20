<h1 align="center">🪣 Bucket</h1>

<p align="center"><b>A fast, portable staging shelf for moving files around Windows.</b></p>

<p align="center">
  Gather files, folders, text, images and links from anywhere into a little always-on-top bucket —
  then copy, move, zip, or drag them out wherever you need. Bucket never touches your files until you say so.
</p>

<p align="center">
  <a href="https://github.com/aungkokomm/Bucket/releases/latest"><img alt="Download" src="https://img.shields.io/badge/Download-Latest%20Release-2F6FED?style=for-the-badge"></a>
  <img alt="Platform" src="https://img.shields.io/badge/Windows%2010%2F11-x64-555?style=for-the-badge">
  <img alt="License" src="https://img.shields.io/badge/License-MIT-1FA855?style=for-the-badge">
</p>

---

## 📸 Screenshots

<!-- Add your screenshots to the docs/ folder and they'll appear here. -->
<p align="center">
  <img src="docs/screenshot-mid.png" alt="Bucket — working view" width="460">
  &nbsp;&nbsp;
  <img src="docs/screenshot-compact.png" alt="Bucket — compact view" width="180">
</p>

---

## Why Bucket?

Moving files on Windows means juggling Explorer windows: open the source, open the destination, drag,
repeat. Bucket gives you a **temporary shelf** to collect things from many places, then deposit them in
one move — copy, move, zip, or drag straight into another app.

It's the kind of "drop‑shelf" that's been a staple on macOS (Dropover, Yoink). Bucket brings it to
Windows — **native, portable, and free**.

> **Your files are safe.** Adding something to a bucket only stores a *reference*. Nothing is copied,
> moved, renamed, or deleted until **you** choose Copy / Move / Export.

## ✨ Features

**Collect anything**
- Drag in, paste (`Ctrl+V`), or use the Add button
- Accepts **files, folders, text snippets, images, and links** — text/images/links are saved as files for you
- **Shake the mouse** to summon a bucket under your cursor
- Optional **screen‑edge catcher** — a tab on the edge you can drop onto
- Global hotkey **`Ctrl+Shift+B`** to bring a bucket up anywhere

**Organize**
- Multiple independent buckets, each a different colour
- **Name** your buckets; jump to any of them from the tray
- Four list views: Mini, Compact, Detailed, Gallery
- **Compact ⇄ expanded** square modes that toggle with a smooth animation
- Filter, reorder, single‑level Undo
- Always‑on‑top toggle and adjustable **window transparency**

**Deposit**
- **Copy To…** / **Move To…** any folder, with conflict handling, progress, and cancel
- **Drag out** to Explorer or any app (drop = copy, `Shift` = move)
- **Quick Destinations** — pin folders for one‑click sends
- **Export transforms:** Copy as **.zip**, **flatten** subfolders, or **batch‑rename & copy**
- Open / Reveal in Explorer / Copy path on any item

**Stays out of the way**
- Lives in the **system tray**; closing the last window tucks it away instead of quitting
- No installer prerequisites, no background services, no databases
- Single per‑user install, no admin required

## ⬇️ Install

1. Download **`Bucket-Setup-1.0.0.exe`** from the [latest release](https://github.com/aungkokomm/Bucket/releases/latest).
2. Run it. It installs per‑user (no admin prompt).
3. Launch **Bucket** from the Start menu.

> Bucket isn't code‑signed, so Windows SmartScreen may show *"Windows protected your PC."*
> Click **More info → Run anyway**. (It's an unsigned indie app, not malware.)

## 🚀 Quick start

| Do this | To… |
|---|---|
| Drag files onto a bucket | Stage them |
| **Shake the mouse** | Summon a bucket under the cursor |
| Press **`Ctrl+Shift+B`** | Show a bucket from anywhere |
| Drag items **out** to a folder | Copy them there ( `Shift` = move ) |
| **Copy To… / Move To…** | Send everything to a folder |
| **Export ▸ Copy as ZIP** | Pack the bucket into one `.zip` |
| Right‑click the title bar | Rename, Settings, Minimize to tray, Close |
| Double‑click the title bar / chevron | Toggle compact ⇄ expanded |

## ⚙️ Settings

Right‑click a bucket's title bar → **Settings…**

- **Keep running in the system tray** — closing the last bucket hides it instead of quitting
- **Restore buckets on next launch** — re‑open buckets that still had items (references only)
- **Shake to summon** — toggle the shake gesture
- **Screen‑edge catcher** — show the edge drop tab (off by default)
- **Window transparency** — see‑through level for bucket windows

## ⌨️ Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+Shift+B` | Summon / show a bucket (global) |
| `Ctrl+V` | Paste into the bucket |
| `Ctrl+N` | New bucket |
| `Ctrl+Z` | Undo last remove/empty |
| `Delete` | Remove selected items |

## 🛠️ Build from source

**Requirements:** Windows 10/11, Visual Studio 2022+ with the **.NET desktop** workload,
.NET 10 SDK, and the WinUI / Windows App SDK tooling.

```powershell
# Clone
git clone https://github.com/aungkokomm/Bucket.git
cd Bucket

# Build & run (debug): open Bucket.csproj in Visual Studio and press F5,
# or from the CLI:
msbuild Bucket.csproj /p:Configuration=Debug /p:Platform=x64 /t:Build

# Build the portable installer (requires Inno Setup 6)
pwsh -File tools\build_installer.ps1
#   → produces dist\Bucket-Setup-1.0.0.exe
```

The app ships as an **unpackaged, self‑contained** WinUI 3 build (no runtime prerequisites),
wrapped by an [Inno Setup](https://jrsoftware.org/isinfo.php) installer.

## 🧰 Tech

- **C# / .NET 10**, **WinUI 3** (Windows App SDK), MVVM with CommunityToolkit.Mvvm
- Self‑contained, unpackaged deployment; Inno Setup for distribution
- No telemetry, no network calls, no background services

## 📄 License

[MIT](LICENSE) © Aung Ko Ko

---

<p align="center"><i>Made for people who move a lot of files.</i></p>
