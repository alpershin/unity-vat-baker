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
- **Shaders** — `VAT/Lit` (forward and deferred), `VAT/Unlit`, and an HLSL entry point for
  Shader Graph (`Runtime/Shaders/VatShaderGraph.hlsl`).
- **LODs** — bake several prefabs as LOD levels into one set; the baker builds the `LODGroup` prefab.

## Requirements

| | |
|---|---|
| Unity | 6000.3 or newer |
| Render pipeline | URP 17.3+ (the shaders are URP-only) |
| Deferred | supported by `VAT/Lit`; URP's deferred path itself rules out the OpenGL backends |
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

Pin a release by appending a tag: `…unity-vat-baker.git#v0.3.0`.

## Bake a character

1. `Assets ▸ Create ▸ VAT ▸ Animation Set`.
2. Assign the **source prefab** — any prefab with a `SkinnedMeshRenderer`.
3. Drop animation clips or whole FBX model files onto the clip list, or import them straight from an
   `AnimatorController` (every clip on a chosen layer, transition durations included).
4. Set FPS, normals, shadows, per-unit animation and the material template, then press **Bake**.

**Compact Position Map** halves the position map — eight bits a channel normalised to the pose
bounds instead of half floats. On the benchmark crowd it cost about 7% of frame time and quantises
to roughly 6 mm on a 1.5 m character. Off by default, because a shader cannot decode a compact map
without the bounds and a Shader Graph that never wired them would break. Turn it on when memory
matters more than those two things; the hand-written shaders get the bounds from the baker
automatically.

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

**Phase Scatter** keeps a crowd on one clip from marching in lockstep. It is seeded from the
instance id, so the renderers have to be GPU-instanced for it to do anything — an un-instanced
renderer plays unshifted rather than picking an arbitrary offset. `VatUnitAnimator` does not use
it: units already desync through their own start times.

Clip fields exposed in the inspector can use `VatClipReference`, which resolves a clip name against
the set and shows a dropdown instead of a raw string.

## Which shader path

| | `VAT/Lit`, `VAT/Unlit` | Shader Graph |
|---|---|---|
| Frame interpolation | yes | yes |
| Crossfade via `VatCrowdPlayer` | yes | yes |
| Per-unit clips (`VatUnitAnimator`) | yes | **no** |
| Deferred (G-buffer) | `VAT/Lit` only | yes, from the Lit target |
| Custom lighting, VFX Graph, Built-In target | no | yes |
| Per-pass fetch savings | yes | no |

The hand-written shaders are the reference implementation and the performance ceiling: they gate
texture fetches per pass and drop vertex inputs nothing reads, neither of which Shader Graph can
express. Per-unit clips arrive as instanced properties, which Shader Graph cannot declare at all.

The graph path is for reaching things the hand-written shaders do not: your own lighting model, a
VFX Graph output, or the Built-In render pipeline.

### Using it from Shader Graph

There is no SubGraph asset in the package yet — wire the two custom functions by hand.

Add a **Custom Function** node, set Type to *File* and Source to
`Runtime/Shaders/VatShaderGraph.hlsl`, then declare the ports exactly in this order:

```
Name: VatSample

in   PositionMap : Texture2D      out  PositionOS : Vector3
     NormalMap   : Texture2D           NormalOS   : Vector3
     BoundsMin   : Vector3
     BoundsMax   : Vector3
     VertexU     : Float
     VatParams   : Vector4
     Phase       : Float
     Time        : Float
```

`BoundsMin` and `BoundsMax` come from `VatAnimationSet.Bounds`. They are **optional**: leave them
unwired and the node reads the position map as-is, which is exactly what a set baked without
**Compact Position Map** needs. Wire them and the same node handles a compact set too — the set
reports a zero-sized box when the map is raw, so one wiring stays correct either way.

`VertexU` is the vertex's column into the maps: a **UV** node set to **UV1**, split, red channel.
The baker writes it to `mesh.uv2`. Drive **Vertex Position** and **Vertex Normal** with the
outputs — the function samples in the vertex stage and has no meaning in the fragment stage.

For `Phase`, add a second Custom Function node from the same file named `VatInstancePhase` — no
inputs, one `Phase` (Float) output — and scale it by however much scatter you want. Shader Graph
has no instance-id node, which is why this exists.

Give the graph's own properties the references the baker looks for, or it will fill nothing and
the crowd will render in bind pose: `_PositionMap`, `_NormalMap`, `_VatParams`, plus `_BaseMap`
and `_BaseColor` if you use the graph as a **Material Template**.

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
