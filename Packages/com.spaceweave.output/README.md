# SpaceWeave Output (`com.spaceweave.output`)

Send a Unity camera as **Equirect / EAC / Cubemap / Fisheye / Cylindrical** into **SpaceWeave** via **Klak Spout** (and optionally **Klak NDI**).

Works in any Unity **2022.3+** Windows project. Does **not** require the Grimsholt CAVE/VR/AR platform scenes.

## Install

### 1. Keijiro scoped registry

Add to your project `Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "Keijiro",
      "url": "https://registry.npmjs.com",
      "scopes": ["jp.keijiro"]
    }
  ],
  "dependencies": {
    "com.spaceweave.output": "file:../../CAVE_Receiver/unity/com.spaceweave.output"
  }
}
```

Adjust the `file:` path to wherever this package lives on disk.  
Spout `2.0.6` and NDI `2.1.6` are pulled in automatically as package dependencies.

**Git URL** (after this folder is on the remote):

```json
"com.spaceweave.output": "https://github.com/sagor995/CAVE_Receiver.git?path=unity/com.spaceweave.output"
```

The host project’s `Packages/manifest.json` must still include the Keijiro scoped registry once (Unity does not always copy scoped registries from nested packages).

Full empty-project checklist: [INSTALL.md](INSTALL.md).

### 2. Import the sample

**Window → Package Manager → SpaceWeave Output → Samples → Output Validation → Import**

### 3. Create / play the sample

**SpaceWeave → Create Sample Scene**, then press Play.  
Or add `SpaceWeaveSampleBootstrap` to Main Camera and Play.

### 4. Connect SpaceWeave desktop

1. Note the Spout name in the Game view status / Console (default `SpaceWeave_EQUIRECT`).
2. Open **Window → SpaceWeave → Spout Output Mirror** to confirm the texture.
3. In SpaceWeave: select that Spout sender.
4. Set **Input format** to match the Unity mode (see table below).

## Mode → Spout name → SpaceWeave format

| Unity mode | Sender suffix | SpaceWeave Input format |
|---|---|---|
| Equirectangular | `_EQUIRECT` | Equirect |
| Cylindrical | `_CYLINDRICAL` | Cylindrical |
| CubemapCross | `_CUBEMAP_CROSS` | Cube Cross |
| CubemapStrip | `_CUBEMAP_STRIP` | Cube Strip |
| Fisheye180 | `_FISHEYE_180` | Fisheye 180 |
| Fisheye360 | `_FISHEYE_360` | Fisheye 360 |
| Eac | `_EAC` | EAC |

Full name = `{senderBaseName}_{SUFFIX}` (default base `SpaceWeave` → e.g. `SpaceWeave_EAC`).

## Minimal wiring (your own scene)

1. Add a Camera.
2. Add **SpaceWeave Output Manager** (`SpaceWeave.Output.SpaceWeaveOutputManager`).
3. Assign **Source Camera**.
4. Add **Klak Spout Sender** on the same GameObject (Capture Method = Texture — the manager forces this).
5. Optional: add **Klak NDI Sender**.
6. Choose **Mode**; Play.

## What this package contains

- Runtime: output manager, contract, fallback pattern, evidence capture, diagnostic rig
- Shaders: Equirect, Cylindrical, Cubemap Pack, Fisheye, EAC, Source Truth
- Editor: Spout Output Mirror + sample scene menu
- Sample: bootstrap + README

## Platforms

- **Spout:** Windows Editor / Standalone (Klak Spout)
- **NDI:** where Klak NDI supports your target

## Grimsholt note

This package is extracted for reuse. Existing Grimsholt `CAVE_Main` / `VR_Main` / `AR_Main` scenes are unchanged. Point Grimsholt at this package later with a `file:` dependency if you want a single source of truth.
