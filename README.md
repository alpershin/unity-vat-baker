# VAT Animation Baker

Bakes Unity animation clips into **vertex animation textures** and plays them back on URP shaders.
A whole crowd then animates from one material and one GPU-instanced draw call — no `Animator`,
no `SkinnedMeshRenderer`, no per-unit CPU work per frame.

- **Editor baker** — a `VatAnimationSet` asset holds the authoring inputs (skinned prefab + clips)
  and the baked outputs (mesh, position/normal maps, clip table) as sub-objects, so a rebake keeps
  every existing reference intact.
- **Preview window** — `Window ▸ VAT ▸ Animation Preview` plays a baked set without entering play mode.
- **Runtime** — `VatCrowdPlayer` drives a whole crowd through global shader properties (one batch),
  `VatUnitAnimator` gives each unit its own clip, start time and crossfade through a
  `MaterialPropertyBlock`.
- **Shaders** — `VAT/Lit`, `VAT/Unlit` and an HLSL entry point for Shader Graph
  (`Runtime/Shaders/VatShaderGraph.hlsl`).
- **LODs** — bake several prefabs as LOD levels into one set; the baker builds the `LODGroup` prefab.

## Requirements

| | |
|---|---|
| Unity | 6000.3 or newer |
| Render pipeline | URP 17.3+ (the shaders are URP-only) |
| Also pulled in | Input System 1.19+, Animation module |

## Install

Package Manager ▸ **Install package from git URL…**

```
https://github.com/alpershin/unity-vat-baker.git
```

Or add it to `Packages/manifest.json` directly:

```json
"com.alpershin.vat": "https://github.com/alpershin/unity-vat-baker.git"
```

Pin a release by appending a tag: `…unity-vat-baker.git#v0.1.0`.

## Bake a character

1. `Assets ▸ Create ▸ VAT ▸ Animation Set`.
2. Assign the **source prefab** — any prefab with a `SkinnedMeshRenderer`.
3. Drop animation clips or whole FBX model files onto the clip list, or import them straight from an
   `AnimatorController` (every clip on a chosen layer, transition durations included).
4. Set FPS, normals, shadows, per-unit animation and the material template, then press **Bake**.

The bake writes the mesh and maps into the set, and drops the materials and a spawnable prefab next
to it. `VatAnimationSet.Prefab` is what a spawner should instantiate.

## Play it back

One clip for the whole crowd — cheapest, single batch, no property blocks:

```csharp
[SerializeField] private VatCrowdPlayer _player;

_player.Play("Run");          // by name
_player.Play(clipIndex);      // or by index
```

Per-unit clips — each unit crossfades on its own, one property block upload per switch:

```csharp
[SerializeField] private VatUnitAnimator _unit;

_unit.Play(1);                // crossfade using the clip's authored transition duration
_unit.PlayImmediate(1);       // no fade
```

`VatUnitAnimator` needs **Per Unit Animation** enabled on the baked material.

Clip fields exposed in the inspector can use `VatClipReference`, which resolves a clip name against
the set and shows a dropdown instead of a raw string.

## Sample

**Crowd Benchmark** (Package Manager ▸ this package ▸ Samples ▸ Import) — a scene that spawns a grid
of characters and switches between a VAT crowd and `SkinnedMeshRenderer` characters at runtime, with
a HUD showing frame time, batches and vertex count.

The sample's skinned comparison variant animates only if the free
[Kevin Iglesias — Human Animations](https://assetstore.unity.com/) clips it was baked from are also
present in the project; the VAT variant is self-contained.

## Layout

```
Runtime/          VatAnimationSet, VatCrowdPlayer, VatUnitAnimator, VatClip, VatClipReference
Runtime/Shaders/  VatLit.shader, VatUnlit.shader, VatCommon.hlsl, VatSampling.hlsl, VatShaderGraph.hlsl
Editor/           VatBaker, VatSampler, VatMeshBuilder, VatPreviewWindow, AnimatorControllerConverter
Samples~/         Crowd Benchmark sample
```

## License

MIT — see [LICENSE.md](LICENSE.md). Third-party art bundled with the sample keeps its own license.
