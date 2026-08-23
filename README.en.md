<div align="center">

<img src="MiamiGraphics.Shell/ui/src/assets/logo/Square310x310.png" width="120" alt="Miami Graphics">

# Miami Graphics

A GTA V graphics-mod launcher and weapon-skin studio.

[Русский](README.md) · [Technical documentation](https://hntenjoyer.github.io/miami-graphics/) (Russian)

Windows · .NET 8 · WPF + WebView2 + React

</div>

---

## What this is

A desktop application that installs graphics mods into GTA V without hand-editing archives.
The user picks a mod from the catalog; the launcher locates the game, backs up the clean
`update.rpf`, computes the difference and writes it into the game's archives. On top of that sit
editors that author content in place: a 3D weapon-skin studio, a crosshair constructor, a minimap
editor, a tracer studio and a PC diagnostics module.

Everything happens at the level of the game's RPF archives: parsing, rebuilding, NG encryption,
checksum repair and streaming-budget accounting. A mistake at that level crashes the game, which is
why most of this code is checks.

---

## Build

```bash
dotnet build MiamiGraphics.Core.sln -c Release
```

The solution holds seven projects: Core, Bridge, Shell, Core.Tests, RageLib, RageLib.GTA5 and
CodeWalker.Core. Installer, Gunpack, DlcLab, RpfShrink and `Tools/*` build separately.

```bash
cd MiamiGraphics.Shell/ui && npm ci && npm run build
```

Requires .NET SDK 8+, Node.js 20+, Windows 10/11 x64.

Tests:

```bash
dotnet test MiamiGraphics.Core.Tests/MiamiGraphics.Core.Tests.csproj
```

### Not in this repository

Some data is not published and is either fetched at runtime or supplied by hand:

| What | Expected at | How to get it |
|---|---|---|
| GTA V encryption keys (`gtav_ng_key.dat`, AES, NG tables) | `MiamiGraphics.Shell/Keys/`, `additionals/keys/` | Downloaded by the app inside `additionals.zip` |
| `ArchiveFix.exe`, `swfmill`, JRE | `additionals/` | Same, at runtime, SHA-256 verified |
| Server side, database migrations, infrastructure configs | — | Not part of the public repository |

The key path is overridable through the MSBuild properties `MiamiKeysDir` (Shell) and `MiamiKitDir`
(RpfShrink).

### Environment variables

No secrets are baked into the source — backend addresses and credentials come from the environment or
from a DPAPI-encrypted `admin-config.bin`.

| Variable | Purpose |
|---|---|
| `MG_SUPABASE_URL` | Direct Supabase project URL |
| `MG_R2_ENDPOINT` | Cloudflare R2 S3 endpoint |
| `MG_DB_VPS_IP`, `MG_EU_VPS_IP` | Origin node IP for the Zapret lists |
| `HG_ADMIN_API_TOKEN` | Admin API token |
| `MIAMI_RENDERER_DIR` | Headless renderer directory |

---

## Layout

| Project | Contents |
|---|---|
| `MiamiGraphics.Core` | Engine: RPF injection, redux parser, gun studio, PC diagnostics, hot-swap, renderer |
| `MiamiGraphics.Shell` | WPF shell, WebView2 bridge, React UI, networking, catalog repositories |
| `MiamiGraphics.Bridge` | JS ↔ C# bridge contracts |
| `MiamiGraphics.Installer` | Standalone installer (`Miami Setup`) |
| `MiamiGraphics.Gunpack` | Local testbed for the weapon-skin editor |
| `MiamiGraphics.DlcLab` | CLI for archive and delta analysis |
| `MiamiGraphics.RpfShrink` | RPF archive compactor |
| `Tools/` | ArchiveTool, MetaTool, TextureTool, YptWriter2015 |
| `RageLib`, `RageLib.GTA5`, `CodeWalker.Core` | Third-party RAGE format libraries |

---

## Working with game archives

**Patch injection (Smart Rebuild).** Applies a `manifest.json` of Replace / Add / Delete actions:
rebuilds every affected root RPF with `.bak` rollback, creates missing nested rpfs from imports, then
re-verifies plain-path writes and rewrites whatever the rebuild dropped. On `update.rpf` it also
strips orphaned declarations out of `content.xml`.
→ `MiamiGraphics.Core/Injector/RpfInjectEngine.cs`

**Streaming-budget gate.** A check on the archive write paths (9 call sites in the shell). Rejects
entries whose content contradicts their name (`RSC7` / `RPF7` magic), computes resource memory from
`SystemFlags` / `GraphicsFlags` and refuses anything above 90 MB against a 100 MB client ceiling — but
first tries to make it fit by recompressing or halving textures.
→ `MiamiGraphics.Core/Services/RpfEntryGate.cs`

**Re-signing, validation and de-obfuscation.** `ArchiveFix.exe` encrypts an OPEN packfile to NG and
recomputes the TOC hashes and header checksum — necessarily staged under the final filename, because
the NG key is derived from that name. `RpfEncryptionCheck` reopens an archive exactly the way the game
does. `NgContainer` unwraps packs with the high `namesLength` bit set.

**In-place editing.** Replace, extract and enumerate files inside a live `update.rpf` and its nested
archives without a full rebuild — addressed by path, by filename or by content signature. Checksums are
fixed child-before-parent.

**Recursive diff.** Walks two RPF trees in parallel, descends into nested archives, NG-decrypts and
inflates every entry to its real bytes before comparing SHA-256. Emits Import / Replace / Delete actions.

**`rpfshrink` compactor.** Rebuilds an RPF7 to drop the holes left by in-place edits, DEFLATE-compresses
entries, recurses into nested archives to depth 8, runs ArchiveFix child-before-parent and verifies
every entry byte for byte.

---

## Installing mods

**Redux catalog.** Grid with search, sorting and cloud-synced favorites; detail page with screenshots
and video, install with cancel, smart uninstall, and 1–5 star reviews with a rating distribution.

**Redux parsing.** Diffing a clean `update.rpf` against a modded one writes a work folder containing
`manifest.json`, `content_info.json`, `component_map.json`, extracted `patch_files/` and per-component
folders. This is the "computing your changes" phase of an install.

**Cross-injection.** Builds a new patch from a base redux plus per-component picks taken from other
parsed reduxes. `timecycle_mods_*.xml` files are merged at modifier level — the name set and ordering
come from the base file and only the donor's values are transferred, because swapping the whole file
desynchronises the game's modifier table.

**Component detection.** Finds the swappable parts — `minimap.gfx`, `hud_reticle.gfx`, ptfx tracers,
`bloodfx.dat`, timecycles, arena and armor (via marker models such as `task_011_u.ydd`) — from
`filesToEnable` in `content.xml`, comparing 8 vanilla RPFs against custom ones.

**Improvements.** Installed as a set: seeds slot containers vanilla lacks, rebuilds the shared nested
slot from its clean base plus each improvement's own files, and generates the
`content.xml` / `setup2.xml` registration with `subPackCount` in `dlc_patch`.

**Overlays.** Self-contained rpfs at the root of `update.rpf`, each registered with an `RPF_FILE` item
in `dataFiles` plus a `filesToEnable` entry in the boot changeSet: parkour spots, green zones, backpack
removal, car logos, fast spawn. Idempotent; foreign copies are detected and stripped.

**Legit check.** Diffs installed files against clean originals and rates every finding
Neutral / Visual / Warning / Danger, including per-field weapon `.meta` diffs (clean value, mod value,
delta %) for owners like `WEAPON_` / `COMPONENT_`. Ends in a Safe / Mixed / Danger verdict.

---

## Weapons

**Gunpacks.** Parses a donor `dlc.rpf` into a gun catalog (`w_ar_`, `w_sg_`, `w_sr_`, `w_mg_`, `w_pi_`,
`w_me_`, `w_lr_` prefixes plus attachment infixes), builds `hntgraph.rpf` from the selected guns and
installs it into `update/x64/dlcpacks/patchday18ng/dlc.rpf` under changeSet
`CCS_PATCHDAY18_NG_STREAMING`.

**Per-gun install.** Assembles `miami_guns_selected.rpf` from a template plus individual gun bytes and
injects it after `miami_weapon.rpf` so it wins `filesToEnable` priority.

### 3D skin studio

A three.js editor embedded in the launcher through WebView2 and a virtual host — no local web server is
started. Gun resources are pulled by HTTP Range out of `pack.zip` with a SHA-keyed cache.

| Tool | Parameters |
|---|---|
| Brush | normal / soft / spray / marker, 2–250 px, strength 5–100% |
| Eraser | restores the original texels |
| Flood fill | tolerance 1–120 |
| Alpha | punches holes through |
| Text | 6 fonts, size 24–800, outline 0–60 px |
| Shapes | line, rectangle, circle, triangle — re-editable after placement |
| Stickers | 5 built-in 256×256 stamps or an uploaded image |

Strokes are clipped to the UV island under the cursor by a mask capped at 2048 px per side. Layers can
be moved, reordered and deleted; the transform HUD gives size 10–600%, rotation ±180°, opacity 5–100%.

**Photo projection.** Projects an image onto the mesh along the camera axes, rasterising each triangle
in texture space with bilinear sampling, a 4% edge fade and backface rejection — producing one seamless
layer per touched texture.

**Tinted glass.** Marks textures as glass (color plus opacity 10–92%). At install time the shader moves
from render bucket 0 to `normal_spec_alpha.sps` (hash 2021887493, bucket 1, mask 0xFF02) and a uniform
alpha clamped to 8..255 is baked into the diffuse.

**3D attachments.** A flat plate cut from an image (2–40 cm, depth 5–45%), or an imported `.glb` merged
into a single geometry (scale 10–400%, yaw ±180°, 65,000-vertex ceiling). Sewn straight into the gun's
own drawable using the GTAV1 declaration (stride 44), rigged to `Gun_Main_Bone` with `BoundsData` rebuilt.

**PNG to solid.** Alpha silhouette (threshold 64) → downscale to 72 px → Moore-neighbour contour trace →
Ramer–Douglas–Peucker simplification (epsilon 1.15) → ear-clipping triangulation → front and back faces
plus hard-normal side walls.

**Animated detail.** A standalone component: a `.ydr` with a 2-bone skeleton (base + movable, tag 417)
on the `default.sps` shader carrying `globalAnimUV0/1` parameters, a looping `.ycd` (UV scroll tracks 17/18, or bone swing/spin
on track 1) at 30 fps clamped to 8–600 frames, a `.ytyp` archetype (flags 0x80600) and a component
`.meta`. Scroll speed 0.5–4, period 0.5–8 s, XYZ rotation ±180°, XYZ offset ±20 cm, mirroring.
10 weapons supported.

**Publishing.** 5 slots per account, name up to 48 characters, description up to 240. Weekly limits:
2 attempts per individual gun, 4 pack-based works, 2 own packs, at most 3 guns in an own pack — checked in the
client against `WorkshopFlowLimits` and confirmed server-side.

---

## Editors

**Minimap.** Recolors every `minimap.gfx` copy in the live archive and applies tweaks on the real gfx
scene frame: a 191×136 canvas with the radar window at X 6–185, Y 8–122. HP and armor digits, bar
layout, `frontend.xml` positions, spliced range rings, big-map packages.

**Crosshair.** Dot size, gap, length, thickness, tilt and outline set separately per weapon group
(all / pistol / SMG / rifle / shotgun), plus opacity and 50–200% scale. One game pixel = 20 twips.
Shared through KNK codes that expire after 30 unused days.

**Tracers and smoke.** Assigns tracer channels per weapon and tunes each channel (three-stop gradient,
thickness, length, smoke color) against a cached base `core.ypt` — staged into a single manifest and a
single `update.rpf` rebuild. Separately, the two ptfx smoke textures can be swapped and `TracerFx`
blanked across `update.rpf` and `dlcpacks`.

**GTA graphics settings.** Two tabs over `settings.xml`: a per-option builder driven by a server-side
optimization catalog, where hovering a card previews that value full-screen and clicking cycles it (an
empty value means "your own value"), and public PRO presets that fill the builder rather than writing
the file directly.

---

## PC diagnostics

The hardware snapshot is taken by 26 independently wrapped collectors that only read WMI, the registry
and `powercfg`; a collector that fails lands in `CollectorErrors` instead of killing the report.

- **Memory slots from raw SMBIOS.** Parsing type-17 structures yields every slot including empty ones,
  with board-style names (A2, B2) and rated versus configured MT/s — which `Win32_PhysicalMemory` alone
  cannot give.
- **Monitors with real names.** EDID name from `WmiMonitorID`, falling back to the raw 0xFC descriptor
  in the registry, plus the driving GPU, resolution, and current and maximum Hz at that same resolution.
- **Hardware tiers for GTA RP.** CPU rated S..D by parsing the model name, flagging X3D, hybrid P+E and
  laptop parts (H/HS/HX step down one tier, U two). Memory rated by channel count, capacity and
  XMP/EXPO state. The drive the game actually sits on is rated NVMe = S, SATA SSD = A, USB = B, HDD = D.
- **Findings engine.** Up to 49 distinct findings with stable ids (`ram-xmp-off`, `game-on-hdd`) and a
  gain range where it is known: XMP off 10–25%, single channel 10–30%. It emits ids and numbers only,
  so the UI localizes the text.
- **One-click apply with an undo journal.** 31 tweaks; the previous state is written to `journal.json`
  before each change, and a Windows restore point is created before the first one.
- **16 proactive tweaks** — MMCSS Games profile, `SystemResponsiveness` 10, mouse acceleration,
  `Win32PrioritySeparation`, `NetworkThrottlingIndex`, HAGS, `GTA5.exe` exclusions.
- **NVIDIA profile through NVAPI.** Direct `nvapi64.dll` DRS calls with no third-party executable:
  `PREFERRED_PSTATE` = prefer max, `PRERENDERLIMIT` = 1, 10 GB shader disk cache.
- **Anti-cheat safety.** Refuses to disable anything RP server admins read as a hidden trace — Prefetch
  and the EventLog service — and instead offers to restore them when another "optimizer" turned them off.
- **Placebo cleanup.** Finds and strips `commandline.txt` flags copied from other guides that do not
  exist in the game: `-high`, `-norestrictions`, `-nomemrestrict`, `-veryhigh`.

---

## Hot-swap for Rockstar mode

Freezes the modded `update\update.rpf` and `dlcpacks\patchday18ng\dlc.rpf` into an image outside the
game folder, swaps mods in when the game starts and swaps the clean files back when it exits.

Five methods decomposed along three axes: agent versus manual trigger, image on the game volume versus
a chosen folder, atomic `File.Replace` versus copy-then-rename. A single table fixes each method's poll
step (250 / 500 / 1000 ms), disarm debounce (3000 or 0 ms) and whether game processes are killed before
the return.

The agent is the same executable relaunched as `--hotswap-agent` from an ONLOGON scheduled task. It
writes a heartbeat every 10 s, retries a failed arm every 10 s and wakes on a WMI
`Win32_ProcessStartTrace` subscription rather than fast polling. Every file operation is preceded by a
journal write of one of five phases, so an interrupted freeze rolls the mod back into the game and a
half-done arm returns the clean file.

Rockstar Games Launcher repairs are watched separately: size and timestamp are compared every 5 s, and
arming is blocked while fresh (under 10 minutes old) `.part` / `.partial` / `.download` / `.rgl_tmp`
files sit next to the targets.

---

## Networking and delivery

**Parallel range downloader.** Multi-connection HTTP Range with lock-free positioned writes: a single
stream below 8 MB, more above (per-node cap: ru2 2, spb1/spb2/msk/hnt/eu/ru 4, everything else 8),
512 KB buffers, a 30 s stall watchdog, retry at
lower parallelism on 503, and an 8 MB-block resume map so switching mirrors does not restart the file.

**DPI bypass.** Splits the TLS ClientHello into 3 flushed fragments so SNI-matching DPI cannot read the
hostname; needs no administrator rights. Optional DNS-over-HTTPS
(Cloudflare / Quad9 / NextDNS) against DNS poisoning. Applied to every launcher `HttpClient` and to
WebView2 native requests.

**Network doctor.** Probes region storage and every node for hot path, cold path (offset 0 and a
mid-file range) and accepted connection count, to say what exactly is broken. Runs from Settings and
automatically below 10 MB/min.

**Verified download.** A single fail-closed SHA-256 checkpoint for everything the launcher puts into the
game: no reference hash means refusal; the reference never comes from the archive itself; bytes land in
a `.part` file renamed only after the check; hashing is streamed in 4 MB chunks.

**Delta self-update.** Downloads only the files whose SHA-256 changed — a few MB instead of the full payload. Any failure falls back to the full installer.

**Install safety.** A process-wide single-writer mutex over `update.rpf` with a 10-minute acquire
timeout, a disk-space preflight reserving a 2 GB OS margin, Restart Manager lookup naming the process
holding a locked file, and startup recovery of orphaned `.rpf.bak` files.

---

## Accounts and catalog

Sign-in by email or username through the `authenticate_user` RPC, then a session token. Two-step
sign-up, password reset and change, email change and profile edits are each confirmed by a 6-digit
emailed code. Closed beta: the code is bound to a computed HWID, and any network failure is treated as
closed.

Catalogs: reduxes and their per-slot versions, gunpacks with guns and variants, settings presets, the
armor library, big maps, optimization groups (cached 5 minutes) and the three featured home slots.
Reviews are one per (item, user) with upsert. An authors directory with following and a feed. Player
builds bundle redux + gunpack + per-slot guns + armor + a settings profile, moderated
submit → pending → approve / reject → resubmit.

HNT-XXXX-XXXX codes export an install state as one snapshot and replay it on another machine. The code
alphabet omits I, O, 0 and 1.

The WebView2 bridge routes **393 named commands** between React and C# as request/response envelopes
plus pushed progress events; messages from foreign origins are ignored and 78 mutating commands are
marked critical.

---

## Interface

React + TypeScript running inside WebView2. Localized into three languages (ru, en, pl): the UI starts in Russian and
falls back to English, while the engine falls back to Russian. `de.json` is in the repository but
neither React nor `Loc.cs` loads it. The skin studio carries its own plain-JS dictionary — four
languages there, Russian fallback, with separate plural rules for Slavic and Germanic languages.

Screens: onboarding and first run, clean-GTA backup (7 steps over 9 backend phases with live speed and
ETA), redux catalog, component customizer, gunpacks, workshop, single components, overlays, GTA graphics
settings, PC diagnostics, legit check, player builds, downloads and installed inventory, application
settings.

---

## Third-party code

`RageLib`, `RageLib.GTA5`, `CodeWalker.Core` and `Libraries/DirectXTex` are third-party libraries for
reading RAGE formats and handling textures. Their license headers are preserved. `ArchiveFix.exe` is
dexyfex's utility.

This project is not affiliated with Rockstar Games or Take-Two Interactive.
