#ifndef PSX_COMMON_INCLUDED
#define PSX_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half4  _EmissionColor;
    half   _AffineAmount;
    half   _SnapAmount;
    half   _Cutoff;
CBUFFER_END

// ---- Globals pushed by PSXManager / PSXFlashlight ----
float4 _PSXSnapRes;      // y = vertical snap grid (x derived from aspect)
half4  _PSXFogColor;
float4 _PSXFogParams;    // x density, y noise amount, z noise scale
float4 _PSXFogWind;      // xyz wind direction * speed
half4  _PSXAmbient;
float4 _PSXColorParams;  // x color levels per channel, y dither strength

float4 _PSXFlashPos;     // xyz position, w = on (1) / off (0)
float4 _PSXFlashDir;     // xyz direction, w = range
float4 _PSXFlashCone;    // x cos(outer), y cos(inner), z intensity
half4  _PSXFlashColor;

// Street lamps, pushed by PSXManager (nearest to the camera first).
#define PSX_MAX_LAMPS 16
float4 _PSXLampPos[PSX_MAX_LAMPS];   // xyz position, w range
float4 _PSXLampColor[PSX_MAX_LAMPS]; // rgb linear color * intensity, w halo strength
float  _PSXLampCount;

// Light a surface with the street lamps. Lamps throw light mostly downward, like a real streetlight.
half3 PSXLamps(float3 positionWS, float3 normalWS)
{
    half3 sum = 0;
    int count = (int)_PSXLampCount;
    for (int i = 0; i < PSX_MAX_LAMPS; i++)
    {
        if (i >= count) break;
        float3 toP = positionWS - _PSXLampPos[i].xyz;
        float d = length(toP);
        float3 L = toP / max(d, 0.0001);
        float atten = saturate(1.0 - d / _PSXLampPos[i].w);
        atten *= atten;
        float ndl = saturate(dot(normalWS, -L)) * 0.85 + 0.15;
        float down = lerp(0.2, 1.0, smoothstep(0.0, 0.8, -L.y));
        sum += _PSXLampColor[i].rgb * atten * ndl * down;
    }
    return sum;
}

// Light scattered toward the camera by fog along the view ray, from each lamp.
// Closed-form integral of 1/r^2 along the ray (what a volumetric ray-march approximates):
// only the stretch of ray in front of the surface counts, so walls occlude the glow,
// and fog between you and the lamp dims it, so halos get bigger and brighter as you approach.
half3 PSXLampScatter(float3 viewDir, float dist)
{
    half3 sum = 0;
    float density = _PSXFogParams.x;
    int count = (int)_PSXLampCount;
    for (int i = 0; i < PSX_MAX_LAMPS; i++)
    {
        if (i >= count) break;
        float3 c = _PSXLampPos[i].xyz - _WorldSpaceCameraPos;
        float tc = dot(c, viewDir);
        float h = max(length(c - viewDir * tc), 0.12);
        float inscatter = (atan((dist - tc) / h) - atan(-tc / h)) / h;
        float lampDist = length(c);
        float fd = lampDist * density * 0.6; // lamps punch through the fog a bit further than walls
        float transmittance = exp(-fd * fd);
        sum += _PSXLampColor[i].rgb * _PSXLampColor[i].w * inscatter * transmittance;
    }
    return sum * density;
}

// Snap a clip-space position to a low-res pixel grid (the PS1 had no sub-pixel precision).
float4 PSXSnapVertex(float4 positionCS)
{
    float gridY = _PSXSnapRes.y > 0 ? _PSXSnapRes.y : 120.0;
    float2 grid = float2(gridY * (_ScreenParams.x / _ScreenParams.y), gridY) * 0.5;
    float4 snapped = positionCS;
    if (positionCS.w > 0.0001)
        snapped.xy = round(positionCS.xy / positionCS.w * grid) / grid * positionCS.w;
    positionCS.xy = lerp(positionCS.xy, snapped.xy, _SnapAmount);
    return positionCS;
}

// ---- Cheap 3D value noise for drifting fog ----
float PSXHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float PSXNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = lerp(PSXHash(i + float3(0,0,0)), PSXHash(i + float3(1,0,0)), f.x);
    float b = lerp(PSXHash(i + float3(0,1,0)), PSXHash(i + float3(1,1,0)), f.x);
    float c = lerp(PSXHash(i + float3(0,0,1)), PSXHash(i + float3(1,0,1)), f.x);
    float d = lerp(PSXHash(i + float3(0,1,1)), PSXHash(i + float3(1,1,1)), f.x);
    return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
}

// Thick, rolling "Silent Hill" fog. Returns fogged color.
half3 PSXApplyFog(half3 color, float3 positionWS)
{
    float3 toPixel = positionWS - _WorldSpaceCameraPos;
    float dist = length(toPixel);
    float3 viewDir = toPixel / max(dist, 0.0001);

    // Two octaves of drifting noise modulate the density so the fog visibly rolls.
    float3 np = positionWS * _PSXFogParams.z + _Time.y * _PSXFogWind.xyz;
    float n = PSXNoise(np) * 0.65 + PSXNoise(np * 2.3 + 17.0) * 0.35;
    float density = _PSXFogParams.x * (1.0 + (n - 0.5) * 2.0 * _PSXFogParams.y);

    float fd = dist * density;
    float fog = 1.0 - exp(-fd * fd); // exp2 fog: clear up close, wall of fog at range

    // Flashlight scattering: the fog glows where the beam is pointing.
    half3 fogColor = _PSXFogColor.rgb;
    if (_PSXFlashPos.w > 0.5)
    {
        float aim = saturate(dot(viewDir, _PSXFlashDir.xyz));
        float glow = pow(aim, 10.0) * 0.08 * _PSXFlashCone.z;
        fogColor += _PSXFlashColor.rgb * glow * saturate(1.0 - dist / max(_PSXFlashDir.w, 0.01));
    }

    return lerp(color, fogColor, saturate(fog)) + PSXLampScatter(viewDir, dist);
}

half3 PSXFlashlight(float3 positionWS, float3 normalWS)
{
    if (_PSXFlashPos.w < 0.5) return 0;

    float3 toP = positionWS - _PSXFlashPos.xyz;
    float d = length(toP);
    float3 L = toP / max(d, 0.0001);

    float cd = dot(L, _PSXFlashDir.xyz);
    float spot = smoothstep(_PSXFlashCone.x, _PSXFlashCone.y, cd);
    // Bright hot-spot ring in the middle, like a cheap reflector.
    float hot = smoothstep(lerp(_PSXFlashCone.y, 1.0, 0.55), lerp(_PSXFlashCone.y, 1.0, 0.75), cd) * 0.6;

    float atten = saturate(1.0 - d / max(_PSXFlashDir.w, 0.01));
    atten *= atten;

    float ndl = saturate(dot(normalWS, -L));
    return _PSXFlashColor.rgb * _PSXFlashCone.z * (spot + hot) * atten * ndl;
}

// Reduce to N levels per channel (32 = PS1 15-bit color) with a 4x4 ordered dither.
half3 PSXQuantize(half3 color, float2 pixelPos)
{
    static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    float levels = _PSXColorParams.x > 1 ? _PSXColorParams.x : 32.0;
    uint2 p = uint2(pixelPos) % 4;
    float dither = (bayer[p.y * 4 + p.x] / 16.0 - 0.5) * _PSXColorParams.y;

    half3 c = LinearToSRGB(saturate(color));
    c = floor(c * (levels - 1.0) + 0.5 + dither) / (levels - 1.0);
    return SRGBToLinear(saturate(c));
}

#endif
