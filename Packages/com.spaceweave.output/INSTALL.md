# Install & verify — empty Unity project

Use this checklist to prove `com.spaceweave.output` works outside Grimsholt.

## Prerequisites

- Unity **2022.3 LTS** (or newer), **Windows** (Spout)
- SpaceWeave desktop build that lists Spout senders
- This package on disk: `…/CAVE_Receiver/unity/com.spaceweave.output/`

## A. Create a clean host project

1. Unity Hub → **New project** → **3D (URP)** or Built-in, Unity 2022.3+.
2. Open `Packages/manifest.json` and merge:

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

Adjust `file:` so it resolves to this package folder from the new project’s `Packages/` directory (example assumes sibling folders under the same parent as `CAVE_Receiver`).

3. Return to the Editor and wait for Package Manager to resolve:
   - `com.spaceweave.output`
   - `jp.keijiro.klak.spout` `2.0.6`
   - `jp.keijiro.klak.ndi` `2.1.6`
4. Confirm **Console** has no compile errors referencing Grimsholt types (`CAVEOutputManager`, `CubemapRendererImproved`, etc.).

## B. Sample → Play → Spout

1. **Window → Package Manager → SpaceWeave Output → Samples → Output Validation → Import**.
2. Open `Assets/Samples/SpaceWeave Output/0.1.0/Output Validation/Scenes/SpaceWeave_Sample`.
3. Confirm Main Camera has `SpaceWeaveOutputManager`, Spout Sender, and NDI Sender (disabled).
4. Press **Play**.
5. Console / on-screen status should show sender `SpaceWeave_EQUIRECT`.
6. **Window → SpaceWeave → Spout Output Mirror** — texture should update each frame (not solid magenta/error).
7. In **SpaceWeave** desktop: select Spout sender `SpaceWeave_EQUIRECT`, set **Input format** to **Equirect**.

If the scene lost script links after import, use **SpaceWeave → Create Sample Scene**.

## C. Mode toggle

1. On `SpaceWeaveOutputManager`, set **Mode** to **Eac**.
2. After the short sender restart, Spout name should be `SpaceWeave_EAC`.
3. In SpaceWeave, select `SpaceWeave_EAC` and set **Input format** to **EAC**.
4. Toggle back to Equirectangular → name returns to `SpaceWeave_EQUIRECT`.

## D. Pass criteria

| Check | Expected |
|---|---|
| Compiles in empty project | Only Spout + NDI + this package |
| Default sender | `SpaceWeave_EQUIRECT` |
| Mode EAC | `SpaceWeave_EAC` + desktop EAC |
| Spout mirror | Live converted texture |
| Grimsholt | Untouched (`CAVE_Main` / `VR_Main` / `AR_Main` not required) |

## Optional: `.unitypackage` export

For teams that cannot use UPM: in a project that already imported the package + sample, use **Assets → Export Package** and include `Packages/com.spaceweave.output` Runtime/Editor/Shaders plus the imported sample assets. Prefer UPM/`file:` or git URL as the primary distribution.

## Static package layout (repo check)

No Unity Editor was available on the packaging machine for a live Play Mode smoke test.
Run this from the package (or repo) to confirm layout + no Grimsholt type leaks:

```powershell
powershell -File unity/com.spaceweave.output/tools/Verify-PackageLayout.ps1
```

Expected: `STATIC_VERIFY_PASS`. Then complete sections A–D above in a clean Unity 2022.3 project for the Spout → SpaceWeave path.
