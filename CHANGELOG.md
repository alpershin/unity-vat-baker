# Changelog

All notable changes to this package are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Requires a rebake.** Normal maps are octahedral `RG16` instead of `RGBAHalf`: a unit vector
  folded onto a square in two channels of eight bits, about a degree of error. Four times smaller —
  an 8000-vertex level with 600 frames drops from 38 MB to 10 MB for the normal map, 77 MB to 48 MB
  for the pair. A set baked before this change lights wrong until rebaked; the geometry is
  unaffected, since position maps are untouched.
- The baker refuses to start if the graphics backend has no `RG16`, rather than letting the format
  reach the driver. An unsupported texture format raises nothing catchable — the backend asserts
  and takes the editor down with it.

### Fixed

- The baker checks the map height. Only the width was validated, so a clip list whose frames summed
  past 16384 rows threw out of `Texture2D` after the sampling had already run, with nothing
  pointing at the clip list. `VatSampler.CountFrames` is now the one place that decides how many
  rows a clip takes, so the guard and the bake cannot round differently.

## [0.2.0] - 2026-08-25

### Fixed

- A per-unit crossfade that had already finished kept costing what a running one costs.
  `VatUnitAnimator` uploads once per switch and never ticks, so the blend duration stayed set and
  the shader's weight pinned at 1 instead of falling back to a single clip — every unit that had
  ever changed clip sampled both clips for the rest of its life. The shader now exits to a single
  clip once the fade is over.
- Phase scatter no longer hashes the unit's world position. A moving unit re-rolled its phase every
  frame, which made the clip jump and the unit shake in place. The seed is now the instance id,
  which survives movement.
- `Crowd Benchmark`: the HUD's live render counters flipped between zero and a doubled reading.
  They are published by the render thread, and with VSync off the main thread outruns it, so
  `LastValue` saw no sample on some frames and two frames' worth on others. They are averaged over
  a frame window now, the way the frame timings already were. The logged sample lines were never
  affected — they divide by frame count, so the zeros and the doubles cancelled.

### Changed

- **Breaking (Shader Graph):** the `VatSample` custom function takes a `Phase` input instead of
  `ObjectOrigin` and `PhaseScatter`. Feed it from the new `VatInstancePhase` function, or zero.
- `VAT/Lit` gained a `UniversalGBuffer` pass. Without one the crowd could only ever be drawn
  forward-only under URP's deferred path, never entering the G-buffer.
- The Shader Graph entry point is no longer a single-sample toy: `VatSample` now interpolates
  between baked frames and crossfades through the `VatCrowdPlayer` globals, so a graph material
  joins the same crowd as a hand-written one. Per-unit clips stay exclusive to `VAT/Lit` and
  `VAT/Unlit` — they arrive as instanced properties, which Shader Graph cannot declare.
- The frame math, the crowd-player globals and clip selection moved into `VatSampling.hlsl`, shared
  by `VatCommon.hlsl` and `VatShaderGraph.hlsl`, so the two paths cannot drift apart on where a
  clip lives.
- Vertex inputs the VAT shaders never read are gone from their `Attributes` structs. Position and
  normal both come out of the maps, so binding the mesh streams fetched bytes nothing consumed:
  the forward pass now reads 16 bytes per vertex instead of 40, the shadow and depth passes 8
  instead of 20. `VAT/Unlit`'s shadow pass keeps the mesh normal — it never samples the normal map
  and needs one for `ApplyShadowBias`. Existing baked meshes work unchanged; no rebake needed.
- Depth-only passes and every `VAT/Unlit` pass no longer sample the normal map.
- The shadow pass snaps to the nearest baked frame instead of interpolating, and no longer compiles
  a `_VAT_FRAME_BLEND` variant. Depth passes keep interpolating, so the depth they write stays
  identical to the forward pass and to the G-buffer.
- A clip crossfade samples both clips at the nearest frame: a transition costs four texture fetches
  instead of eight.

## [0.1.0] - 2026-08-25

### Added

- Initial package release, extracted from the VAT Bake Test project.
- `VatAnimationSet` authoring asset: skinned prefab plus clips in, mesh and position/normal maps out
  as sub-objects of the asset.
- Editor baker with LOD level support, material and prefab generation, and clip import from an
  `AnimatorController` layer.
- `Window ▸ VAT ▸ Animation Preview` window for inspecting a baked set outside play mode.
- Runtime playback: `VatCrowdPlayer` (whole crowd through global shader properties) and
  `VatUnitAnimator` (per-unit clip and crossfade through a `MaterialPropertyBlock`).
- URP shaders `VAT/Lit` and `VAT/Unlit`, plus a Shader Graph HLSL entry point.
- `Crowd Benchmark` sample.
