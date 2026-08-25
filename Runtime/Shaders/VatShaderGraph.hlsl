#ifndef VAT_SHADERGRAPH_INCLUDED
#define VAT_SHADERGRAPH_INCLUDED

// Shader Graph entry point. The frame math, the crowd-player globals and the clip selection all
// come from VatSampling.hlsl, the same header VatCommon.hlsl uses — so this path and the
// hand-written VAT/Lit and VAT/Unlit cannot quietly disagree about where a clip lives.
//
// What this path deliberately does NOT carry: per-unit clips. They arrive as instanced properties,
// which Shader Graph has no way to declare. A crowd that needs its own clip per unit stays on the
// hand-written shaders.

#include "VatSampling.hlsl"

void VatGraphFetch(
    UnityTexture2D positionMap,
    UnityTexture2D normalMap,
    float vertexU,
    float frame,
    float height,
    out float3 positionOS,
    out float3 normalOS)
{
    float2 uv = VatMapUv(vertexU, frame, height);

    positionOS = SAMPLE_TEXTURE2D_LOD(positionMap.tex, positionMap.samplerstate, uv, 0).xyz;
    normalOS = VatDecodeNormal(SAMPLE_TEXTURE2D_LOD(normalMap.tex, normalMap.samplerstate, uv, 0).xy);
}

// One texel row, no interpolation between frames.
void VatGraphSampleNearest(
    UnityTexture2D positionMap,
    UnityTexture2D normalMap,
    float vertexU,
    float4 clipParams,
    float time,
    float phase,
    out float3 positionOS,
    out float3 normalOS)
{
    float frame = floor(VatFrame(clipParams, time, phase));
    VatGraphFetch(positionMap, normalMap, vertexU, frame, max(clipParams.w, 1.0), positionOS, normalOS);
}

// The pose between the two neighbouring baked frames.
void VatGraphSampleClip(
    UnityTexture2D positionMap,
    UnityTexture2D normalMap,
    float vertexU,
    float4 clipParams,
    float time,
    float phase,
    out float3 positionOS,
    out float3 normalOS)
{
    float frame = VatFrame(clipParams, time, phase);
    float height = max(clipParams.w, 1.0);

    float frame0, frame1, weight;
    VatFramePair(clipParams, frame, frame0, frame1, weight);

    float3 position0, normal0, position1, normal1;
    VatGraphFetch(positionMap, normalMap, vertexU, frame0, height, position0, normal0);
    VatGraphFetch(positionMap, normalMap, vertexU, frame1, height, position1, normal1);

    positionOS = lerp(position0, position1, weight);
    normalOS = lerp(normal0, normal1, weight);
}

// Custom Function node, File mode, function name "VatSample", precision Float.
// Feed Time from the Time node and Phase from the VatInstancePhase node below.
// Drive Vertex Position and Vertex Normal of the graph with the outputs.
//
// VatParams is the clip baked into the material. A VatCrowdPlayer in the scene overrides it
// through the globals, so a graph material joins the same crowd as a hand-written one.
//
// Phase used to be derived here from an Object node -> Position. That is wrong for anything that
// moves: the hash re-rolls as the unit walks and the animation jumps every frame. Pass a phase
// that is stable per unit, or zero for a crowd that may march in lockstep.
void VatSample_float(
    UnityTexture2D PositionMap,
    UnityTexture2D NormalMap,
    float VertexU,
    float4 VatParams,
    float Phase,
    float Time,
    out float3 PositionOS,
    out float3 NormalOS)
{
    float4 clipA = VatCurrentClip(VatParams);

    if (_VatBlend <= 0.0)
    {
        VatGraphSampleClip(PositionMap, NormalMap, VertexU, clipA, Time, Phase, PositionOS, NormalOS);
        return;
    }

    // Both clips drop to the nearest frame while the fade runs: two poses melting into each other
    // already hide the step between baked frames, so interpolating inside each of them doubles the
    // fetches for something nobody can see.
    float3 targetPosition, targetNormal;
    VatGraphSampleNearest(PositionMap, NormalMap, VertexU, clipA, Time, Phase, PositionOS, NormalOS);
    VatGraphSampleNearest(PositionMap, NormalMap, VertexU, _VatClipB, Time, Phase, targetPosition, targetNormal);

    PositionOS = lerp(PositionOS, targetPosition, _VatBlend);
    NormalOS = lerp(NormalOS, targetNormal, _VatBlend);
}

// Custom Function node, File mode, function name "VatInstancePhase", precision Float.
// Shader Graph has no instance-id node, so this is how a graph reaches the same stable per-unit
// scatter VAT/Lit and VAT/Unlit use. Scale the output by your own scatter amount, then feed it
// into VatSample's Phase.
void VatInstancePhase_float(out float Phase)
{
#if UNITY_ANY_INSTANCING_ENABLED
    Phase = VatPhase(unity_InstanceID);
#else
    Phase = 0.0;
#endif
}

#endif
