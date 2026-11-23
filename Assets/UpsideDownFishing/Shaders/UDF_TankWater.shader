// Water inside the room aquarium (a box, origin at the bottom centre, size _Size; its "top"
// face is the water surface - mount the object upside down for an anti-gravity tank).
// The view ray's path through the box is computed analytically per pixel (slab test), so the
// water gets bluer/denser where you look through more of it — no depth texture needed.
// Top face: rippling surface with fresnel sky reflection + lamp glints. Sides: absorption tint,
// faint caustic shimmer and a bright meniscus line at the waterline.
Shader "UDF/TankWater"
{
    Properties
    {
        _Size ("Box Size (m)", Vector) = (0.7, 0.36, 0.38, 0)
        _ShallowColor ("Shallow Colour", Color) = (0.42, 0.9, 0.95, 1)
        _DeepColor ("Deep Colour", Color) = (0.05, 0.45, 0.68, 1)
        _Absorb ("Absorption (1/m)", Float) = 2.2
        _MinAlpha ("Min Alpha", Range(0,1)) = 0.08
        _MaxAlpha ("Max Alpha", Range(0,1)) = 0.72
        _SkyColor ("Reflected Sky", Color) = (0.85, 0.97, 1, 1)
        _Ripple ("Ripple Strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "UDF_Underwater.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Size;
                half4 _ShallowColor, _DeepColor, _SkyColor;
                half _Absorb, _MinAlpha, _MaxAlpha, _Ripple;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                return o;
            }

            // gentle sum-of-sines height field on the top surface, returns d(h)/dx, d(h)/dz
            float2 RippleSlope(float2 p, float t)
            {
                float2 g = 0;
                const float2 d1 = float2(0.8, 0.6), d2 = float2(-0.5, 0.86), d3 = float2(0.1, -1.0);
                g += d1 * cos(dot(p, d1) * 38.0 + t * 2.6) * 0.06;
                g += d2 * cos(dot(p, d2) * 55.0 + t * 3.4) * 0.045;
                g += d3 * cos(dot(p, d3) * 81.0 + t * 4.3) * 0.03;
                return g * _Ripple;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 camOS = TransformWorldToObject(GetCameraPositionWS());
                float3 ro = camOS;
                float3 rd = normalize(i.positionOS - camOS);
                float3 bmin = float3(-_Size.x * 0.5, 0, -_Size.z * 0.5);
                float3 bmax = float3(_Size.x * 0.5, _Size.y, _Size.z * 0.5);
                float3 inv = 1.0 / (rd + (abs(rd) < 1e-5) * 1e-5);
                float3 t0 = (bmin - ro) * inv, t1 = (bmax - ro) * inv;
                float3 tmax3 = max(t0, t1);
                float tFar = min(min(tmax3.x, tmax3.y), tmax3.z);
                float tHere = length(i.positionOS - camOS);
                float thickness = max(0, tFar - tHere);

                // exit point height: looking down through the water reaches the deep bottom
                float3 exitP = ro + rd * tFar;
                float deep = saturate(1.0 - exitP.y / max(_Size.y, 0.01)) * 0.5 + saturate(thickness * 1.2) * 0.5;
                half3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep);
                half alpha = lerp(_MinAlpha, _MaxAlpha, 1.0 - exp(-_Absorb * thickness));

                float t = _Time.y;
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                if (i.normalOS.y > 0.5)
                {
                    // surface seen from above: ripples, fresnel sky, lamp glints
                    float2 s = RippleSlope(i.positionOS.xz, t);
                    // in world space: the box may be mounted upside down (anti-gravity tank),
                    // then this "top" face is the downward-facing surface seen from below
                    float3 n = normalize(TransformObjectToWorldDir(float3(-s.x, 1, -s.y)));
                    float fres = pow(1.0 - saturate(dot(n, V)), 4.0) * 0.9 + 0.04;
                    col = lerp(col, _SkyColor.rgb, fres);
                    float3 L = normalize(TransformObjectToWorldDir(float3(0.2, 1.0, 0.1)));
                    float spec = pow(saturate(dot(reflect(-L, n), V)), 180.0) * 1.6;
                    col += spec;
                    alpha = saturate(max(alpha, 0.3) + fres * 0.5 + spec);
                }
                else
                {
                    // sides: caustic shimmer (dimmer lower down) + bright meniscus line
                    float h01 = saturate(i.positionOS.y / max(_Size.y, 0.01));
                    float c = UDF_Caustics(i.positionWS * float3(1.4, 1, 1.4) + float3(0, 0, i.positionWS.y * 0.6), 3.0);
                    col += c * 0.12 * h01;
                    float men = smoothstep(_Size.y - 0.012, _Size.y - 0.002, i.positionOS.y);
                    col = lerp(col, half3(0.92, 1, 1), men * 0.75);
                    alpha = max(alpha, men * 0.8);
                    float fres = pow(1.0 - saturate(abs(dot(normalize(TransformObjectToWorldDir(i.normalOS)), V))), 3.0);
                    alpha = saturate(alpha + fres * 0.15);
                }
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
