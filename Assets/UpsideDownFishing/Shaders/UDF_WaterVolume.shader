// The sea above the ceiling, seen from below through the water surface. Front faces are
// culled so the player sees the inside of the volume — but there is NO visible sea floor:
// the ceiling is the water SURFACE, and above it the sea simply fades into depth.
//
//   * Beer-Lambert absorption over the real path length through the water (red dies first):
//     bright sunlit water near the surface, deep blue further away.
//   * Volumetric light shafts: a few samples along the view ray through drifting beam noise.
Shader "UDF/WaterVolume"
{
    Properties
    {
        _DeepWater ("Deep Water Colour", Color) = (0.02,0.22,0.45,1)
        _ShallowWater ("Sunlit Water Colour", Color) = (0.30,0.78,0.95,1)
        _ShaftColor ("Light Shaft Colour", Color) = (0.65,0.97,1,1)
        _Absorption ("Absorption RGB (1/m)", Vector) = (0.55,0.24,0.16,0)
        _ShaftIntensity ("Shaft Intensity", Float) = 0.55
        _SurfaceY ("Surface Y", Float) = 2.6
        _Depth ("Depth", Float) = 1.6
        _Alpha ("Alpha", Range(0,1)) = 0.95
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepWater, _ShallowWater, _ShaftColor;
                float4 _Absorption;
                float _ShaftIntensity;
                float _SurfaceY, _Depth, _Alpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float beams(float2 p, float t)
            {
                float s = sin(p.x * 2.3 + t * 0.35) * sin(p.y * 1.9 - t * 0.27 + 1.3);
                s += 0.6 * sin((p.x + p.y) * 3.7 - t * 0.5) * sin((p.x - p.y) * 2.9 + t * 0.4);
                return saturate(s * 0.5 + 0.15);
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y;
                float3 cam = GetCameraPositionWS();
                float3 p = i.positionWS;
                float3 ray = p - cam;
                float total = length(ray);
                float3 dir = ray / max(total, 1e-4);

                // where the view ray enters the water (the surface plane)
                float tEnter = dir.y > 1e-3 ? saturate((_SurfaceY - cam.y) / (ray.y)) : 0;
                float pathLen = max(total * (1.0 - tEnter), 0.0);
                float3 entry = cam + ray * tEnter;

                // ---- no sea floor: the far faces of the volume are never "seen" as surfaces.
                // Looking up through the ceiling you only see water fading into depth:
                // bright sunlit water near the surface, deep blue further away.
                float3 transmittance = exp(-_Absorption.xyz * pathLen);
                half3 col = _ShallowWater.rgb * transmittance + _DeepWater.rgb * (1.0 - transmittance);
                // a soft glow toward straight up (sun high above the inverted sea)
                col += _ShallowWater.rgb * pow(saturate(dir.y), 6.0) * 0.12;

                // ---- light shafts: 4 samples along the in-water segment
                float shaft = 0;
                [unroll] for (int s = 0; s < 4; s++)
                {
                    float f = (s + 0.5) / 4.0;
                    float3 q = lerp(entry, p, f);
                    float hFade = 1.0 - saturate((q.y - _SurfaceY) / max(_Depth, 0.01));
                    // beams slant slightly: sample the pattern at the point they entered the surface
                    float2 bq = q.xz - float2(0.25, 0.15) * (q.y - _SurfaceY);
                    shaft += beams(bq * 1.4, t) * hFade;
                }
                shaft = shaft / 4.0 * saturate(pathLen * 0.8);
                col += _ShaftColor.rgb * shaft * _ShaftIntensity;

                return half4(col, _Alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
