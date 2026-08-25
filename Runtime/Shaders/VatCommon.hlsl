#ifndef VAT_COMMON_INCLUDED
#define VAT_COMMON_INCLUDED

// Include this AFTER the shader's UnityPerMaterial CBUFFER: the functions below read
// _VatParams and _PhaseScatter out of it.

#include "VatSampling.hlsl"

TEXTURE2D(_PositionMap);    SAMPLER(sampler_PositionMap);
TEXTURE2D(_NormalMap);      SAMPLER(sampler_NormalMap);

// Published by VatCrowdPlayer as globals, so the whole crowd stays on one material and one batch.
// All zero means no player in the scene: the material plays the clip it was baked with.
float4 _VatClipA;
float4 _VatClipB;
float _VatBlend;

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

float4 VatCurrentClip()
{
    return _VatClipA.y > 0.0 ? _VatClipA : _VatParams;
}

float3 VatFetchPosition(float vertexU, float frame, float height)
{
    return SAMPLE_TEXTURE2D_LOD(_PositionMap, sampler_PositionMap, VatMapUv(vertexU, frame, height), 0).xyz;
}

float3 VatFetchNormal(float vertexU, float frame, float height)
{
    return SAMPLE_TEXTURE2D_LOD(_NormalMap, sampler_NormalMap, VatMapUv(vertexU, frame, height), 0).xyz;
}

// The two neighbouring frames of a clip and how far we are between them.
void VatFramePair(float4 clipParams, float frame, out float frame0, out float frame1, out float weight)
{
    float count = max(clipParams.y, 1.0);
    float local = frame - clipParams.x;
    float floored = floor(local);

    weight = local - floored;
    frame0 = clipParams.x + fmod(floored, count);
    frame1 = clipParams.x + fmod(floored + 1.0, count);
}

void VatSampleClip(float vertexU, float4 clipParams, float time, float phase, out float3 positionOS, out float3 normalOS)
{
    float frame = VatFrame(clipParams, time, phase);
    float height = max(clipParams.w, 1.0);

#if defined(_VAT_FRAME_BLEND)
    float frame0, frame1, weight;
    VatFramePair(clipParams, frame, frame0, frame1, weight);
    positionOS = lerp(VatFetchPosition(vertexU, frame0, height), VatFetchPosition(vertexU, frame1, height), weight);
    normalOS = lerp(VatFetchNormal(vertexU, frame0, height), VatFetchNormal(vertexU, frame1, height), weight);
#else
    float single = floor(frame);
    positionOS = VatFetchPosition(vertexU, single, height);
    normalOS = VatFetchNormal(vertexU, single, height);
#endif
}

// Crossfading two clips is a straight vertex lerp, not a real animation blend: keep transitions
// short or the pose melts on the way over.
void VatSampleShared(float vertexU, out float3 positionOS, out float3 normalOS)
{
    float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    float phase = VatPhase(origin) * _PhaseScatter;

    VatSampleClip(vertexU, VatCurrentClip(), _Time.y, phase, positionOS, normalOS);

    if (_VatBlend > 0.0)
    {
        float3 targetPosition;
        float3 targetNormal;
        VatSampleClip(vertexU, _VatClipB, _Time.y, phase, targetPosition, targetNormal);
        positionOS = lerp(positionOS, targetPosition, _VatBlend);
        normalOS = lerp(normalOS, targetNormal, _VatBlend);
    }
}

#if defined(_VAT_PER_INSTANCE)
void VatSampleUnit(float4 clipA, float vertexU, out float3 positionOS, out float3 normalOS)
{
    float4 clipB = UNITY_ACCESS_INSTANCED_PROP(VatProps, _VatUnitClipB);
    float4 times = UNITY_ACCESS_INSTANCED_PROP(VatProps, _VatUnitTimes);

    // The desync comes from each unit's own start time here, not from its position.
    VatSampleClip(vertexU, clipA, _Time.y - times.x, 0.0, positionOS, normalOS);

    float weight = times.w > 0.0 ? saturate((_Time.y - times.z) / times.w) : 0.0;
    if (weight > 0.0)
    {
        float3 targetPosition;
        float3 targetNormal;
        VatSampleClip(vertexU, clipB, _Time.y - times.y, 0.0, targetPosition, targetNormal);
        positionOS = lerp(positionOS, targetPosition, weight);
        normalOS = lerp(normalOS, targetNormal, weight);
    }
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
