# Changelog

All notable changes to this package are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
