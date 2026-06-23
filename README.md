<h1 align="center">🪣 Bucket</h1>

<p align="center">
  <b>The colour-coded drop-shelf for Windows.</b><br>
  Run a separate bucket for every job — pile things in now, deal with them later.
</p>

<p align="center">
  <a href="https://github.com/aungkokomm/Bucket/releases/latest"><img alt="Download" src="https://img.shields.io/badge/Download-Latest%20Release-2F6FED?style=for-the-badge"></a>
  <img alt="Platform" src="https://img.shields.io/badge/Windows%2010%2F11-x64-555?style=for-the-badge">
  <img alt="License" src="https://img.shields.io/badge/License-MIT-1FA855?style=for-the-badge">
</p>
<img width="1536" height="1024" alt="buket_banner" src="https://github.com/user-attachments/assets/c464c703-bb59-49e9-8278-8f567e2f1156" />

<p align="center">
  <img width="605" alt="Bucket toolbar" src="https://github.com/user-attachments/assets/4689f1e4-af6e-4bf2-aa04-d26ad0fe7072" />
  <br><br>
  <img width="405" alt="Bucket window" src="https://github.com/user-attachments/assets/be9b0995-e838-442a-aad6-79ed305e621a" />
</p>

---

## You know this dance

You're gathering files for an email, a report, a backup. A few are on the Desktop. A couple are in
Downloads. One's buried three folders deep. So you open Explorer windows, line them up side by side,
drag carefully, and hope nothing lands in the wrong place.

**Bucket replaces that whole dance with a little shelf that floats on top of everything.**

As you come across things you want, just toss them into the bucket — a file here, a folder there, a
screenshot, a line of text, a link. They wait there in a tidy pile. When you're ready, send the whole
pile somewhere in **one move**: copy it to a folder, zip it up, or drag it straight into another app.

> ### 🛟 Your files stay exactly where they are
> Dropping something into a bucket just makes a little pointer to it — **nothing is copied, moved,
> renamed, or deleted** until *you* choose to. Change your mind? Empty the bucket and your files
> haven't budged.

---

## 🌈 One pile per job, each its own colour — *this* is the difference

Every other drop-shelf hands you **one** pile, and everything you grab lands in the same heap. Bucket
gives you a **whole set of colour-coded buckets, side by side**.

Run a **blue** bucket for "email to Sarah", a **green** one for "back up to the NAS", an **orange**
one for "this week's photos" — all on screen at the same time. The **colour is the label**: you know
at a glance which pile is which, and things from different jobs never get mixed together. Give each one
a name too, and jump straight to any open bucket from the system tray.

Need another? One click spins up a fresh bucket in the next colour:

<p align="center"><b>🔵 Blue → 🟢 Green → 🟠 Orange → 🟣 Purple → 🔴 Red</b></p>

> ### 💡 Mid-collect and a new idea hits?
> You're busy filling one bucket when you spot a *completely different* batch you need to grab — right
> now, before it slips your mind. Don't break your flow to deal with it: just open another bucket, drop
> the new things in, and carry on. **Your job is to spot and grab; the buckets hold on to everything
> else.** One idea, one bucket — as many as the moment needs.

It turns "I'm juggling files" into "I have a tidy, colour-coded station for each thing I'm doing."

---

## What else you can do

### 🧺 Collect from anywhere
Drag things in, or paste them (`Ctrl+V`). Bucket takes **files, folders, text, images, and links** —
and turns loose text, images, and links into real files so you can use them later. Need a bucket
*right now*? **Give your mouse a shake** and one appears under your cursor, or press **`Ctrl+Shift+B`**
from any app.

### 📤 Send it wherever — your way
- **Copy** or **move** the whole bucket to any folder, with a progress bar and no surprises
- **Drag the pile out** to Explorer or any app — drop to copy, hold **Shift** to move
- Pin your favourite folders as **one-click destinations**
- Or transform on the way out: pack everything into a **`.zip`**, **flatten** a mess of subfolders
  into one place, or **rename them in sequence** as you copy

### 🫥 Stays out of your way
Bucket lives quietly in the system tray. Close the last window and it just tucks away instead of
quitting — your piles are still there when you come back. It's portable, installs in seconds without
admin rights, and never phones home.

---

## Get it

1. **[Download the latest release](https://github.com/aungkokomm/Bucket/releases/latest)** and run the installer.
2. It installs just for you — **no admin prompt**, no fuss.
3. Open **Bucket** from the Start menu and drag your first file in. 🎉

> 💡 Because Bucket is a free indie app (not signed with a paid certificate), Windows may show a blue
> *"Windows protected your PC"* screen the first time. Click **More info → Run anyway** — it's safe.

---

## The everyday moves

| You do this | …and Bucket does this |
|---|---|
| Drag files onto a bucket | Adds them to the pile (your originals stay put) |
| Shake the mouse | Pops a bucket up under your cursor |
| Press `Ctrl+Shift+B` | Brings a bucket forward from anywhere |
| Drag the pile **out** to a folder | Copies it there (hold **Shift** to move instead) |
| **Copy To… / Move To…** | Sends everything to a folder you pick |
| **Export ▸ Copy as ZIP** | Packs the whole bucket into one `.zip` |
| Double-click the title bar | Toggles between the small square and the full view |
| Right-click the title bar | Rename, Settings, Minimize to tray, About |

**Handy shortcuts:** `Ctrl+V` paste · `Ctrl+N` new bucket · `Ctrl+Z` undo · `Delete` remove selected

---

## A few things you can tweak

Right-click a bucket's title bar → **Settings**:

- **Keep running in the tray** so closing the last bucket doesn't quit the app
- **Reopen your buckets** the next time you launch (pointers only — your files are untouched)
- Turn the **shake-to-summon** gesture on or off
- Show a **screen-edge tab** you can drop onto (off by default)
- Make bucket windows a little **see-through** so you can keep an eye on what's behind them

---

<details>
<summary><b>For developers</b> — build it yourself</summary>

<br>

Bucket is a native **C# / WinUI 3** app (.NET 10, Windows App SDK), built with the
CommunityToolkit MVVM toolkit. It ships as a self-contained, unpackaged build — no runtime
prerequisites — wrapped in an [Inno Setup](https://jrsoftware.org/isinfo.php) installer. No telemetry,
no network calls, no background services.

```powershell
git clone https://github.com/aungkokomm/Bucket.git
cd Bucket

# Run it (debug): open Bucket.csproj in Visual Studio and press F5, or:
msbuild Bucket.csproj /p:Configuration=Debug /p:Platform=x64 /t:Build

# Build the portable installer (needs Inno Setup 6):
pwsh -File tools\build_installer.ps1   # → dist\Bucket-Setup-1.0.0.exe
```

**Requirements:** Windows 10/11, Visual Studio 2022+ with the **.NET desktop** workload, the .NET 10
SDK, and the WinUI / Windows App SDK tooling.

</details>

---

<p align="center">
  <b>Made for people who move a lot of files.</b><br>
  MIT licensed · © 2026 Aung Ko Ko · <a href="https://aungkokomm.github.io/">more of my apps</a>
</p>
