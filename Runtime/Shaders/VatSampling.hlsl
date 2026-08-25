#ifndef VAT_SAMPLING_INCLUDED
#define VAT_SAMPLING_INCLUDED

// vatParams: x = start frame, y = frame count, z = fps, w = map height (total baked frames)

// Current frame of the clip, wrapped into its own range.
float VatFrame(float4 vatParams, float time, float phase01)
{
    float count = max(vatParams.y, 1.0);
    float elapsed = time * max(vatParams.z, 0.0) + phase01 * count;
    return vatParams.x + fmod(elapsed, count);
}

// Stable per-instance phase, so a crowd of identical units does not march in lockstep.
// Derived from the instance origin, which keeps every unit on the same material and batch.
float VatPhase(float3 worldOrigin)
{
    return frac(sin(dot(worldOrigin.xz, float2(12.9898, 78.233))) * 43758.5453);
}

// Column = vertex index (baked into TEXCOORD1.x), row = frame, sampled at the texel center.
float2 VatMapUv(float vertexU, float frame, float mapHeight)
{
    return float2(vertexU, (frame + 0.5) / max(mapHeight, 1.0));
}

#endif
