#ifndef VAT_SHADERGRAPH_INCLUDED
#define VAT_SHADERGRAPH_INCLUDED

#include "VatSampling.hlsl"

// Custom Function node, File mode, function name "VatSample", precision Float.
// Feed ObjectOrigin from Object node -> Position, and Time from the Time node.
// Drive Vertex Position and Vertex Normal of the graph with the outputs.
void VatSample_float(
    UnityTexture2D PositionMap,
    UnityTexture2D NormalMap,
    float VertexU,
    float4 VatParams,
    float PhaseScatter,
    float3 ObjectOrigin,
    float Time,
    out float3 PositionOS,
    out float3 NormalOS)
{
    float phase = VatPhase(ObjectOrigin) * PhaseScatter;
    float frame = VatFrame(VatParams, Time, phase);
    float2 uv = VatMapUv(VertexU, frame, VatParams.w);

    PositionOS = SAMPLE_TEXTURE2D_LOD(PositionMap.tex, PositionMap.samplerstate, uv, 0).xyz;
    NormalOS = SAMPLE_TEXTURE2D_LOD(NormalMap.tex, NormalMap.samplerstate, uv, 0).xyz;
}

#endif
