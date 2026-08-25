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

// Stable per-unit phase, so a crowd of identical units does not march in lockstep.
//
// The seed has to be something that does not change while a unit walks. Hashing the world origin
// reads correctly on a parked crowd and falls apart the moment it moves: the phase re-rolls every
// frame, the clip jumps somewhere else each time, and the unit shakes in place. An instance id
// survives movement.
//
// Wang hash rather than the usual sin/frac: instance ids arrive sequential, and sequential seeds
// must not come out as a visible ramp across the crowd.
float VatPhase(uint seed)
{
    uint hash = (seed ^ 61u) ^ (seed >> 16u);
    hash *= 9u;
    hash = hash ^ (hash >> 4u);
    hash *= 0x27d4eb2du;
    hash = hash ^ (hash >> 15u);
    return float(hash & 0x00ffffffu) / float(0x01000000u);
}

// Column = vertex index (baked into TEXCOORD1.x), row = frame, sampled at the texel center.
float2 VatMapUv(float vertexU, float frame, float mapHeight)
{
    return float2(vertexU, (frame + 0.5) / max(mapHeight, 1.0));
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

// Published by VatCrowdPlayer as globals, so the whole crowd stays on one material and one batch.
// All zero means no player in the scene: the material plays the clip it was baked with.
//
// These live here, alongside the frame math, because both the hand-written shaders and the Shader
// Graph entry point need them and neither can own them without the other drifting.
float4 _VatClipA;
float4 _VatClipB;
float _VatBlend;

/// The clip the crowd player published, or the one baked into the material when there is no player.
float4 VatCurrentClip(float4 materialParams)
{
    return _VatClipA.y > 0.0 ? _VatClipA : materialParams;
}

#endif
