# Crowd Benchmark

Spawns a grid of characters and switches between two variants at runtime:

- **VAT** — one instanced material, `VatUnitAnimator` per unit.
- **Skinned** — plain `SkinnedMeshRenderer` + `Animator`, for comparison.

Open `Benchmark.unity`, press play, and use the HUD to change the crowd size and variant.

Contents:

- `Benchmark.unity` — the scene.
- `BakedSet/` — a pre-baked `VatAnimationSet` with its mesh, maps, materials and spawnable prefab.
- `Scripts/` — grid spawner, HUD, frame stats and the variant switcher.
- `Job-Stickmans-Character-Pack-SS/` — the source character (free Asset Store pack).
- `Animations/Base.controller` — animator controller for the skinned variant.

The skinned variant plays its clip only if the animation pack the set was baked from
(Kevin Iglesias — Human Animations) is present in the project; without it the character
spawns in bind pose. The VAT variant needs nothing beyond this sample.
