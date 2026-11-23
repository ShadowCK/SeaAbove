// Shared "inside the upside-down sea" lighting: Beer-Lambert absorption over the view path
// through the water, projected caustics, and the sea's own light (which enters through the
// surface at the BOTTOM of the sea, i.e. it shines upward from the room side).
// Globals are set by CeilingWater.Build(); _UDF_WaterOn = 0 disables everything.
#ifndef UDF_UNDERWATER_INCLUDED
#define UDF_UNDERWATER_INCLUDED

float  _UDF_WaterOn;
float  _UDF_SurfaceY;
float  _UDF_SeaDepth;
float4 _UDF_DeepWater;      // rgb
float4 _UDF_Absorption;     // rgb, 1/m
float4 _UDF_SeaLightDir;    // xyz = direction TO the light (points down, toward the surface)
float4 _UDF_SeaLightColor;

float2 udf_hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float udf_causticLayer(float2 p, float t)
{
    float2 g = floor(p), f = frac(p);
    float d1 = 8, d2 = 8;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 o = float2(x, y);
        float2 r = 0.5 + 0.45 * sin(t + 6.2831 * udf_hash22(g + o));
        float d = length(o + r - f);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
    }
    return d2 - d1;
}

// 0..~1.5 bright caustic network, projected along the light onto world xz
float UDF_Caustics(float3 positionWS, float scale)
{
    float t = _Time.y;
    float2 p = positionWS.xz * scale;
    float a = udf_causticLayer(p + float2(t * 0.11, t * 0.07), t * 0.9);
    float b = udf_causticLayer(p * 1.37 + float2(-t * 0.08, t * 0.12) + 4.3, t * 1.1 + 2.0);
    float ca = saturate(1.0 - smoothstep(0.0, 0.18, a));
    float cb = saturate(1.0 - smoothstep(0.0, 0.22, b));
    return ca * ca * 0.6 + cb * cb * 0.4 + ca * cb * 0.8;
}

// Length of the view ray inside the water (the camera is always below the surface).
float UDF_WaterPath(float3 positionWS)
{
    float3 cam = GetCameraPositionWS();
    float3 ray = positionWS - cam;
    if (positionWS.y <= _UDF_SurfaceY || ray.y <= 1e-4) return 0;
    float tEnter = saturate((_UDF_SurfaceY - cam.y) / ray.y);
    return length(ray) * (1.0 - tEnter);
}

// Applies absorption + in-scattering. Returns col unchanged above the water / when disabled.
half3 UDF_ApplyWaterFog(half3 col, float3 positionWS)
{
    float path = UDF_WaterPath(positionWS) * _UDF_WaterOn;
    float3 T = exp(-_UDF_Absorption.xyz * path);
    return col * T + _UDF_DeepWater.rgb * (1.0 - T);
}

// Lighting for a surface inside the sea: wrapped light from the surface side + caustics +
// blue ambient that darkens with depth.
half3 UDF_SeaLighting(half3 albedo, float3 n, float3 positionWS, half smoothness, half causticStrength)
{
    float3 L = normalize(_UDF_SeaLightDir.xyz);
    float ndl = saturate(dot(n, L) * 0.6 + 0.4);                     // soft wrap
    float depth01 = saturate((positionWS.y - _UDF_SurfaceY) / max(_UDF_SeaDepth, 0.01));
    float c = UDF_Caustics(positionWS, 3.2) * saturate(dot(n, L) + 0.3) * lerp(1.0, 0.35, depth01);
    half3 lightCol = _UDF_SeaLightColor.rgb * lerp(1.0, 0.55, depth01);
    half3 ambient = lerp(half3(0.36, 0.62, 0.72), half3(0.18, 0.38, 0.52), depth01);
    half3 col = albedo * (ambient + lightCol * ndl * 0.8 + lightCol * c * causticStrength);

    float3 V = normalize(GetCameraPositionWS() - positionWS);
    float3 H = normalize(L + V);
    float spec = pow(saturate(dot(n, H)), lerp(8.0, 128.0, smoothness)) * smoothness * 0.35;
    col += lightCol * spec;
    return col;
}

// Lighting inside the room aquarium: bright aqua ambient (the tank is shallow and lit), a
// warm-white key light and caustics. upSign = +1: lamp straight above the tank (normal tank);
// -1: the light enters through the downward-facing water surface (upside-down tank).
half3 UDF_TankLighting(half3 albedo, float3 n, float3 positionWS, half smoothness, half causticStrength, float upSign)
{
    float3 L = normalize(float3(0.2, upSign, 0.1));
    float ndl = saturate(dot(n, L) * 0.55 + 0.45);
    float c = UDF_Caustics(positionWS * float3(1.6, 1, 1.6), 3.2) * saturate(dot(n, L) + 0.35);
    half3 lightCol = half3(1.0, 0.97, 0.9);
    half3 ambient = half3(0.34, 0.58, 0.66);
    half3 col = albedo * (ambient + lightCol * ndl * 0.75 + half3(0.8, 0.95, 1.0) * c * causticStrength * 0.8);
    float3 V = normalize(GetCameraPositionWS() - positionWS);
    float3 H = normalize(L + V);
    col += lightCol * pow(saturate(dot(n, H)), lerp(8.0, 128.0, smoothness)) * smoothness * 0.4;
    return col;
}

#endif
