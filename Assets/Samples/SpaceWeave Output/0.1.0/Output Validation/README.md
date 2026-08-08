# SpaceWeave Output — Sample

## Quick start

1. Import this sample from **Package Manager → SpaceWeave Output → Samples → Output Validation**.
2. Open `Scenes/SpaceWeave_Sample` and press **Play**.
3. Optional: **Window → SpaceWeave → Spout Output Mirror** to preview the exact sender texture.
4. In **SpaceWeave** desktop: select Spout sender `SpaceWeave_EQUIRECT` and set **Input format** to **Equirect**.

The sample scene already contains:

- Main Camera + `SpaceWeaveOutputManager` (Equirectangular, base name `SpaceWeave`)
- Klak **Spout Sender** (Capture Method = Texture)
- Klak **NDI Sender** (present, **disabled** by default)
- Directional light, ground plane, diagnostic rig
- `SpaceWeaveSampleBootstrap` as a safety net if components were stripped

If the imported scene is missing senders, use **SpaceWeave → Create Sample Scene**.

## Mode → Spout name → SpaceWeave format

| Unity `SpaceWeaveOutputManager.mode` | Spout / NDI name (default base) | SpaceWeave Input format |
|---|---|---|
| Equirectangular | `SpaceWeave_EQUIRECT` | Equirect |
| Cylindrical | `SpaceWeave_CYLINDRICAL` | Cylindrical |
| CubemapCross | `SpaceWeave_CUBEMAP_CROSS` | Cube Cross |
| CubemapStrip | `SpaceWeave_CUBEMAP_STRIP` | Cube Strip |
| Fisheye180 | `SpaceWeave_FISHEYE_180` | Fisheye 180 |
| Fisheye360 | `SpaceWeave_FISHEYE_360` | Fisheye 360 |
| Eac | `SpaceWeave_EAC` | EAC |

Change **Mode** on the manager; the sender name updates after a short restart. Re-select the new name in SpaceWeave if needed.

## NDI

Enable the `NdiSender` component on Main Camera if you use NDI instead of (or as well as) Spout.
