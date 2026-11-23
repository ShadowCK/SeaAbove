// The inverted water surface that replaces the real ceiling, seen from below.
// Target look: a sunlit tropical shallow sea — bright azure, a sharp bright refraction network,
// sparkles, and a thick glossy lip that sags down along the walls with crashing foam.
//
//   * 5 peaked travelling waves (exp-sine) displace the tessellated surface; the same height
//     field gives exact per-pixel normals. Capillary octaves add only to the normal.
//   * Rim lip: within _RimWidth of the wall the surface bulges down by up to _RimDrop.
//     uv.y = metres to the wall, uv2.xy = unit direction toward the nearest wall.
//   * Schlick Fresnel, bright sunlit refraction network (UDF_Caustics), sparkles.
//   * Damped ring-wave packets (bobber splash, fish jumps) perturb the per-pixel normal.
Shader "UDF/CeilingWater"
{
    Properties
    {
        [Header(Colour)]
        _WaterTint ("Transmission Tint", Color) = (0.30,0.80,0.98,0.32)
        _DeepTint ("Grazing / Deep Tint", Color) = (0.06,0.46,0.80,0.86)
        _ReflectColor ("Reflection Colour", Color) = (0.86,0.95,1,1)
        _ScatterColor ("Light Colour", Color) = (0.75,1,1,1)
        _FoamTint ("Foam Colour", Color) = (0.97,1,1,0.95)

        [Header(Waves)]
        _WaveHeight ("Wave Height (m)", Float) = 0.022
        _WaveLength ("Main Wavelength (m)", Float) = 1.6
        _WaveDir ("Main Direction (deg)", Float) = 25
        _Choppiness ("Crest Sharpness", Range(0.5,3)) = 1.6
        _WaveTimeScale ("Wave Speed Multiplier", Float) = 0.8
        _DetailStrength ("Capillary Normal Strength", Range(0,2)) = 0.6

        [Header(Light)]
        _CausticNet ("Sunlit Refraction Network", Range(0,3)) = 0.85
        _NetScale ("Network Scale", Float) = 1.6
        _Sparkle ("Sparkles", Range(0,3)) = 0.9
        _SpecDir ("Glint Light Dir (to light)", Vector) = (0.35,-1,0.25,0)
        _Gloss ("Glint Sharpness", Float) = 900
        _SpecIntensity ("Glint Intensity", Float) = 0.5
        _ScatterIntensity ("Crest Scatter", Float) = 0.9
        _SunDir ("Sea-side Light Dir (to light)", Vector) = (-0.2,1,0.3,0)

        [Header(Rim and Foam)]
        _RimDrop ("Rim Lip Drop (m)", Float) = 0.07
        _RimWidth ("Rim Lip Width (m)", Float) = 0.38
        _FoamBand ("Foam Width (m)", Float) = 0.16
        _EdgeAlphaFade ("Edge Fade (m)", Float) = 0.015
        _RippleHeight ("Ripple Height (m)", Float) = 0.005
        _RippleSpeed ("Ripple Speed (m/s)", Float) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "UDF_Underwater.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WaterTint, _DeepTint, _ReflectColor, _ScatterColor, _FoamTint;
                float _WaveHeight, _WaveLength, _WaveDir, _Choppiness, _WaveTimeScale, _DetailStrength;
                float _CausticNet, _NetScale, _Sparkle;
                float4 _SpecDir, _SunDir;
                float _Gloss, _SpecIntensity, _ScatterIntensity;
                float _RimDrop, _RimWidth, _FoamBand, _EdgeAlphaFade, _RippleHeight, _RippleSpeed;
            CBUFFER_END
            float4 _UDF_Ripples[4];   // xyz = world pos, w = start time (global, set by CeilingWater)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 wallDir : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float height : TEXCOORD2;
                float2 wallDir : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            static const float kGravity = 9.81;

            void AddWave(float2 xz, float2 dir, float wavelength, float amp, float t, inout float h, inout float2 grad)
            {
                float k = 6.2831853 / wavelength;
                float w = sqrt(kGravity * k) * _WaveTimeScale;
                float ph = k * dot(dir, xz) - w * t;
                float e = exp(_Choppiness * (sin(ph) - 1.0));
                h += amp * e;
                grad += amp * e * _Choppiness * cos(ph) * k * dir;
            }

            float2 Dir(float deg) { float r = radians(deg); return float2(cos(r), sin(r)); }

            void Waves(float2 xz, float t, out float h, out float2 grad)
            {
                h = 0; grad = 0;
                float L = _WaveLength, A = _WaveHeight, d = _WaveDir;
                AddWave(xz, Dir(d),        L,        A,        t, h, grad);
                AddWave(xz, Dir(d + 38),   L * 0.61, A * 0.55, t, h, grad);
                AddWave(xz, Dir(d - 47),   L * 0.43, A * 0.38, t, h, grad);
                AddWave(xz, Dir(d + 101),  L * 0.29, A * 0.22, t, h, grad);
                AddWave(xz, Dir(d - 152),  L * 0.19, A * 0.13, t, h, grad);
            }

            void Ripples(float2 xz, float t, out float h, out float2 grad)
            {
                h = 0; grad = 0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float age = t - _UDF_Ripples[i].w;
                    if (age > 0 && age < 3.0)
                    {
                        float2 dv = xz - _UDF_Ripples[i].xz;
                        float d = length(dv) + 1e-4;
                        float x = d - age * _RippleSpeed;
                        float env = exp(-x * x * 60.0) * exp(-age * 1.3) / (1.0 + d * 4.0);
                        float k = 55.0;
                        float s = sin(x * k), c = cos(x * k);
                        h += _RippleHeight * env * s;
                        grad += _RippleHeight * env * (k * c - 120.0 * x * s) * (dv / d);
                    }
                }
            }

            // rounded lip: drop * (1 - e/w)^2 near the wall, with slow rolling swell along it
            float RimHeight(float edge, float2 xz, float t, out float dHdEdge)
            {
                float w = max(_RimWidth, 0.01);
                float u = saturate(1.0 - edge / w);
                float swell = 1.0 + 0.22 * sin(dot(xz, float2(4.1, 3.3)) + t * 1.7) * sin(dot(xz, float2(-2.3, 5.2)) - t * 1.1);
                dHdEdge = -2.0 * _RimDrop * swell * u / w;
                return _RimDrop * swell * u * u;
            }

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float t = _Time.y;
                float h; float2 g;
                Waves(ws.xz, t, h, g);
                float pin = smoothstep(0.0, 0.2, v.uv.y);
                float dR;
                float rim = RimHeight(v.uv.y, ws.xz, t, dR);
                float disp = (h - _WaveHeight * 0.5) * pin + rim;
                ws.y -= disp;                                   // toward the viewer = down
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv;
                o.height = (h - _WaveHeight * 0.5) * pin;
                o.wallDir = v.wallDir;
                return o;
            }

            float hash12(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash12(i), hash12(i + float2(1, 0)), u.x), lerp(hash12(i + float2(0, 1)), hash12(i + 1), u.x), u.y);
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 xz = i.positionWS.xz;
                float edge = i.uv.y;
                float pin = smoothstep(0.0, 0.2, edge);

                // ---- normal: waves + ripples + capillaries + the rim lip
                float h, hr; float2 g, gr;
                Waves(xz, t, h, g);
                Ripples(xz, t, hr, gr);
                g = (g + gr) * pin;
                float2 d1 = Dir(_WaveDir + 70), d2 = Dir(_WaveDir - 20);
                float p1 = dot(d1, xz) * 41.0 - t * 5.1, p2 = dot(d2, xz) * 67.0 - t * 6.7;
                g += _DetailStrength * (cos(p1) * 0.006 * 41.0 * d1 + cos(p2) * 0.0028 * 67.0 * d2) * 0.12;
                g += _DetailStrength * (float2(vnoise(xz * 23 + t * 0.9), vnoise(xz * 23 - t * 0.8 + 17)) - 0.5) * 0.18;
                float dR;
                float rim = RimHeight(edge, xz, t, dR);
                float2 wd = i.wallDir;
                if (dot(wd, wd) > 0.01) g += normalize(wd) * (-dR);  // edge shrinks toward the wall

                float3 N = normalize(float3(-g.x, -1.0, -g.y));
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                if (dot(N, V) < 0) N = -N;
                float NdotV = saturate(dot(N, V));
                float fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
                float grazing = 1.0 - NdotV;

                // ---- transmission: bright azure, deeper blue at grazing angles
                half3 trans = lerp(_WaterTint.rgb, _DeepTint.rgb, smoothstep(0.15, 0.9, grazing));

                // ---- sunlit refraction network: sharp bright lines bent by the surface slope
                float3 netP = float3(xz.x + g.x * 0.35, 0, xz.y + g.y * 0.35);
                float net = UDF_Caustics(netP, _NetScale);
                float net2 = UDF_Caustics(netP * 1.9 + 3.1, _NetScale);
                trans += _ScatterColor.rgb * (net * 0.75 + net2 * 0.35) * _CausticNet * (1.0 - grazing * 0.6);

                // ---- sparkles: tiny bright glints where capillaries face the viewer
                float sp = vnoise(xz * 70.0 + g * 40.0 + t * 1.5) * vnoise(xz * 53.0 - t * 1.1);
                trans += _ScatterColor.rgb * smoothstep(0.55, 0.85, sp) * _Sparkle;

                // ---- light through thin crests
                float3 Ls = normalize(_SunDir.xyz);
                float crest = saturate(i.height / max(_WaveHeight, 1e-4) + 0.4);
                trans += _ScatterColor.rgb * pow(saturate(dot(V, -Ls) * 0.5 + 0.5), 3.0) * crest * _ScatterIntensity * 0.35;

                // ---- reflection + glint
                float3 R = reflect(-V, N);
                half3 refl = _ReflectColor.rgb * lerp(0.7, 1.0, saturate(-R.y));
                float3 Lg = normalize(_SpecDir.xyz);
                float spec = min(pow(saturate(dot(N, normalize(Lg + V))), _Gloss) * _SpecIntensity, 0.8);

                half3 col = lerp(trans, refl, fresnel) + spec;
                half a = lerp(_WaterTint.a, _DeepTint.a, smoothstep(0.1, 0.9, grazing));
                a = max(a, fresnel);
                a = saturate(a + spec * 0.3 + net * 0.15);

                // ---- foam: rolling bands along the lip, broken by noise, plus fresh splashes
                float n1 = vnoise(xz * 14 + t * 0.35), n2 = vnoise(xz * 41 - t * 0.6);
                float foamNoise = n1 * 0.65 + n2 * 0.35;
                float band = _FoamBand * (0.55 + 0.9 * n1);
                float rolling = 0.5 + 0.5 * sin(edge * 55.0 - t * 2.6 + n1 * 6.0);    // waves rolling into the wall
                float foam = smoothstep(band, band * 0.2, edge) * smoothstep(0.3, 0.6, foamNoise + 0.2 * rolling);
                foam = max(foam, smoothstep(0.035, 0.0, edge) * 0.9);                   // bright meniscus line
                float rippleFoam = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float age = t - _UDF_Ripples[k].w;
                    if (age > 0 && age < 1.0)
                    {
                        float d = distance(xz, _UDF_Ripples[k].xz);
                        float x = (d - age * _RippleSpeed) * 45.0;
                        float ring = exp(-x * x) * 0.6 + exp(-d * d * 900.0) * (1.0 - age);
                        rippleFoam += ring * (1.0 - age) * smoothstep(0.4, 0.7, foamNoise);
                    }
                }
                foam = saturate(foam + rippleFoam * 0.5);
                // the lip catches light: brighter, more opaque
                float lip = saturate(rim / max(_RimDrop, 1e-4));
                col += _ScatterColor.rgb * lip * 0.25;
                a = lerp(a, 0.95, lip * 0.5);
                col = lerp(col, _FoamTint.rgb, foam * _FoamTint.a);
                a = lerp(a, 1.0, foam * _FoamTint.a);

                a *= smoothstep(0.0, _EdgeAlphaFade, edge);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
