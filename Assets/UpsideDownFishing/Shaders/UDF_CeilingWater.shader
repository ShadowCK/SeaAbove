// The inverted water surface that replaces the real ceiling.
// Seen from below: animated stylized waves, caustic sparkles, foam rim (uv.y = distance
// to edge, 0 at the wall) and up to 4 expanding ripples (bobber splash, fish jumps).
Shader "UDF/CeilingWater"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.35,0.85,0.95,0.55)
        _DeepColor ("Deep Color", Color) = (0.12,0.35,0.75,0.75)
        _FoamColor ("Foam Color", Color) = (1,1,1,0.95)
        _HighlightColor ("Highlight", Color) = (0.85,1,1,1)
        _WaveScale ("Wave Scale", Float) = 2.2
        _WaveSpeed ("Wave Speed", Float) = 0.35
        _FoamWidth ("Foam Width (m)", Float) = 0.12
        _EdgeFade ("Edge Fade (m)", Float) = 0.04
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

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor, _HighlightColor;
                float _WaveScale, _WaveSpeed, _FoamWidth, _EdgeFade;
            CBUFFER_END
            float4 _UDF_Ripples[4];   // xyz = world pos, w = start time (global)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            float2 hash2(float2 p) { p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3))); return frac(sin(p) * 43758.5453); }

            // Voronoi edges -> cartoon caustic lines
            float voronoiEdge(float2 p, float t)
            {
                float2 g = floor(p), f = frac(p);
                float d1 = 8, d2 = 8;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 o = float2(x, y);
                    float2 r = hash2(g + o);
                    r = 0.5 + 0.5 * sin(t + 6.2831 * r);
                    float d = length(o + r - f);
                    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                return d2 - d1;
            }

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 xz = i.positionWS.xz;
                float t = _Time.y;
                float edge = i.uv.y; // metres to the wall

                float c1 = voronoiEdge(xz * _WaveScale + float2(t * _WaveSpeed, t * _WaveSpeed * 0.6), t * 0.8);
                float c2 = voronoiEdge(xz * _WaveScale * 1.7 - float2(t * _WaveSpeed * 0.7, -t * _WaveSpeed), t * 1.1 + 3);
                float caustic = smoothstep(0.12, 0.0, c1) * 0.7 + smoothstep(0.08, 0.0, c2) * 0.5;

                float tint = 0.5 + 0.5 * sin(xz.x * 1.3 + t * 0.4) * sin(xz.y * 1.1 - t * 0.3);
                half4 col = lerp(_DeepColor, _ShallowColor, tint);
                col.rgb = lerp(col.rgb, _HighlightColor.rgb, saturate(caustic));

                // ripples
                for (int k = 0; k < 4; k++)
                {
                    float age = t - _UDF_Ripples[k].w;
                    if (age > 0 && age < 2.5)
                    {
                        float d = distance(xz, _UDF_Ripples[k].xz);
                        float r = age * 0.6;
                        float ring = smoothstep(0.035, 0.0, abs(d - r)) + smoothstep(0.03, 0.0, abs(d - r * 0.6)) * 0.6;
                        col.rgb = lerp(col.rgb, _FoamColor.rgb, ring * (1 - age / 2.5));
                    }
                }

                // foam rim with wobbly boundary
                float wobble = sin(xz.x * 9 + t * 1.5) * sin(xz.y * 7 - t * 1.2) * 0.03;
                float foam = smoothstep(_FoamWidth + wobble, _FoamWidth * 0.6 + wobble, edge);
                col = lerp(col, _FoamColor, foam);
                col.a *= smoothstep(0.0, _EdgeFade, edge);
                return col;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
