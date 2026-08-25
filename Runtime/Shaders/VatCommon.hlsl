#ifndef VAT_COMMON_INCLUDED
#define VAT_COMMON_INCLUDED

// Include this AFTER the shader's UnityPerMaterial CBUFFER: the functions below read
// _VatParams and _PhaseScatter out of it.
//
// Two defines let a pass buy less than the full sampling before including this file:
//
//   VAT_POSITION_ONLY   the pass never reads the normal (depth-only, every unlit pass), so the
//                       normal map is never sampled. Position math is untouched, which keeps the
//                       depth this pass writes bit-identical to the forward pass.
//   VAT_NEAREST_FRAME   the pass snaps to the nearest baked frame instead of interpolating.
//                       Shadow maps only: it shifts the pose by up to half a frame, which is
//                       invisible in a shadow but would tear a depth prepass against forward.

#include "VatSampling.hlsl"

TEXTURE2D(_PositionMap);    SAMPLER(sampler_PositionMap);
TEXTURE2D(_NormalMap);      SAMPLER(sampler_NormalMap);

#if defined(_VAT_PER_INSTANCE)
// Per-unit mode: every unit carries its own two clip slots and its own timings, so it can switch
// and crossfade on its own schedule. Costs the SRP Batcher and the GPU Resident Drawer: these
// arrive through a MaterialPropertyBlock and the crowd falls back to classic GPU instancing.
UNITY_INSTANCING_BUFFER_START(VatProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _VatUnitClipA)
    UNITY_DEFINE_INSTANCED_PROP(float4, _VatUnitClipB)
    UNITY_DEFINE_INSTANCED_PROP(float4, _VatUnitTimes)  // x: A start, y: B start, z: blend start, w: blend duration
UNITY_INSTANCING_BUFFER_END(VatProps)
#endif

// The instance id exists only when the draw is actually instanced. A single un-instanced renderer
// has nothing to scatter against, so it plays unshifted rather than picking an arbitrary offset.
float VatUnitPhase()
{
#if UNITY_ANY_INSTANCING_ENABLED
    return VatPhase(unity_InstanceID) * _PhaseScatter;
#else
    return 0.0;
#endif
}

float3 VatFetchPosition(float vertexU, float frame, float height)
{
    float3 encoded = SAMPLE_TEXTURE2D_LOD(_PositionMap, sampler_PositionMap, VatMapUv(vertexU, frame, height), 0).xyz;
    return VatDecodePosition(encoded, _VatBoundsMin.xyz, _VatBoundsMax.xyz);
}

float3 VatFetchNormal(float vertexU, float frame, float height)
{
    float2 encoded = SAMPLE_TEXTURE2D_LOD(_NormalMap, sampler_NormalMap, VatMapUv(vertexU, frame, height), 0).xy;
    return VatDecodeNormal(encoded);
}

// Every normal fetch in the sampling chain goes through here, so a position-only pass drops them
// all through one guard instead of the chain being written out twice and drifting apart.
float3 VatFetchNormalOrZero(float vertexU, float frame, float height)
{
#if defined(VAT_POSITION_ONLY)
    return 0.0;
#else
    return VatFetchNormal(vertexU, frame, height);
#endif
}

// One texel row, no interpolation between frames. Half the fetches of the interpolating path.
void VatSampleClipNearest(float vertexU, float4 clipParams, float time, float phase, out float3 positionOS, out float3 normalOS)
{
    float frame = floor(VatFrame(clipParams, time, phase));
    float height = max(clipParams.w, 1.0);

    positionOS = VatFetchPosition(vertexU, frame, height);
    normalOS = VatFetchNormalOrZero(vertexU, frame, height);
}

void VatSampleClip(float vertexU, float4 clipParams, float time, float phase, out float3 positionOS, out float3 normalOS)
{
#if defined(_VAT_FRAME_BLEND) && !defined(VAT_NEAREST_FRAME)
    float frame = VatFrame(clipParams, time, phase);
    float height = max(clipParams.w, 1.0);

    float frame0, frame1, weight;
    VatFramePair(clipParams, frame, frame0, frame1, weight);
    positionOS = lerp(VatFetchPosition(vertexU, frame0, height), VatFetchPosition(vertexU, frame1, height), weight);
    normalOS = lerp(VatFetchNormalOrZero(vertexU, frame0, height), VatFetchNormalOrZero(vertexU, frame1, height), weight);
#else
    VatSampleClipNearest(vertexU, clipParams, time, phase, positionOS, normalOS);
#endif
}

// Crossfading two clips is a straight vertex lerp, not a real animation blend: keep transitions
// short or the pose melts on the way over.
//
// Both clips go through the nearest-frame path while the fade runs. Two poses melting into each
// other already hide the step between baked frames, so interpolating inside each of them doubles
// the fetches for something nobody can see: a transition costs four, not eight.
void VatSampleShared(float vertexU, out float3 positionOS, out float3 normalOS)
{
    float phase = VatUnitPhase();

    if (_VatBlend <= 0.0)
    {
        VatSampleClip(vertexU, VatCurrentClip(_VatParams), _Time.y, phase, positionOS, normalOS);
        return;
    }

    float3 targetPosition;
    float3 targetNormal;
    VatSampleClipNearest(vertexU, VatCurrentClip(_VatParams), _Time.y, phase, positionOS, normalOS);
    VatSampleClipNearest(vertexU, _VatClipB, _Time.y, phase, targetPosition, targetNormal);

    positionOS = lerp(positionOS, targetPosition, _VatBlend);
    normalOS = lerp(normalOS, targetNormal, _VatBlend);
}

#if defined(_VAT_PER_INSTANCE)
void VatSampleUnit(float4 clipA, float vertexU, out float3 positionOS, out float3 normalOS)
{
    float4 times = UNITY_ACCESS_INSTANCED_PROP(VatProps, _VatUnitTimes);
    float weight = times.w > 0.0 ? saturate((_Time.y - times.z) / times.w) : 0.0;

    // The desync comes from each unit's own start time here, not from its position.
    if (weight <= 0.0)
    {
        VatSampleClip(vertexU, clipA, _Time.y - times.x, 0.0, positionOS, normalOS);
        return;
    }

    // A finished fade is never cleared: VatUnitAnimator uploads once per switch and never ticks,
    // so times.w stays set and weight pins at 1 forever. Without this exit, every unit that has
    // ever changed clip keeps sampling both of them for the rest of its life — double the fetches
    // to lerp its way back to clip B, which is where it already was.
    if (weight >= 1.0)
    {
        VatSampleClip(vertexU, UNITY_ACCESS_INSTANCED_PROP(VatProps, _VatUnitClipB), _Time.y - times.y, 0.0, positionOS, normalOS);
        return;
    }

    float4 clipB = UNITY_ACCESS_INSTANCED_PROP(VatProps, _VatUnitClipB);
    float3 targetPosition;
    float3 targetNormal;
    VatSampleClipNearest(vertexU, clipA, _Time.y - times.x, 0.0, positionOS, normalOS);
    VatSampleClipNearest(vertexU, clipB, _Time.y - times.y, 0.0, targetPosition, targetNormal);

    positionOS = lerp(positionOS, targetPosition, weight);
    normalOS = lerp(normalOS, targetNormal, weight);
}
#endif

void VatSampleVertex(float vertexU, out float3 positionOS, out float3 normalOS)
{
#if defined(_VAT_PER_INSTANCE)
    // A unit whose property block was never filled (no VatUnitAnimator on it) would otherwise
    // freeze on frame zero. Fall back to the shared material clip instead.
    float4 clipA = UNITY_ACCESS_INSTANCED_PROP(VatProps, _VatUnitClipA);
    if (clipA.y > 0.0)
    {
        VatSampleUnit(clipA, vertexU, positionOS, normalOS);
    }
    else
    {
        VatSampleShared(vertexU, positionOS, normalOS);
    }
#else
    VatSampleShared(vertexU, positionOS, normalOS);
#endif

    normalOS = SafeNormalize(normalOS);
}

float3 VatPositionOS(float vertexU)
{
    float3 positionOS;
    float3 normalOS;
    VatSampleVertex(vertexU, positionOS, normalOS);
    return positionOS;
}

#endif
