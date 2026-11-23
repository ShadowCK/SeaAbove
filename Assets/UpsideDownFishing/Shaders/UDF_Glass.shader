// Aquarium glass panels. Nearly clear face-on, brighter and more reflective at grazing angles
// (fresnel), a fake room/window reflection streak from the reflected view direction, and a
// bright polished edge where uv approaches the panel border. Both sides are drawn.
Shader "UDF/Glass"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.78, 0.95, 1, 1)
        _BaseAlpha ("Face-on Alpha", Range(0,1)) = 0.05
        _FresnelAlpha ("Grazing Alpha", Range(0,1)) = 0.45
        _EdgeWidth ("Edge Width (uv)", Range(0,0.2)) = 0.02
        _Streaks ("Reflection Streaks", Range(0,1)) = 0.35
        _Highlight ("Highlight (0..1)", Range(0,1)) = 0
        _HighlightColor ("Highlight Colour", Color) = (1, 0.82, 0.3, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+12" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
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
                half4 _Tint, _HighlightColor;
                half _BaseAlpha, _FresnelAlpha, _EdgeWidth, _Streaks, _Highlight;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS) * (front ? 1 : -1);
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                float ndv = saturate(dot(n, V));
                float fres = pow(1.0 - ndv, 4.0);

                // fake environment: two soft "window" streaks + brighter upper hemisphere
                float3 R = reflect(-V, n);
                float streak = smoothstep(0.08, 0.0, abs(frac(R.x * 1.3 + R.z * 0.7 + 0.15) - 0.5) - 0.32)
                             + 0.6 * smoothstep(0.04, 0.0, abs(frac(R.x * 2.1 - R.z * 0.4 + 0.6) - 0.5) - 0.44);
                streak *= saturate(R.y * 1.5 + 0.6) * _Streaks;
                float sky = saturate(R.y * 0.5 + 0.5);

                float2 e = min(i.uv, 1.0 - i.uv);
                float edge = 1.0 - smoothstep(0.0, _EdgeWidth, min(e.x, e.y));

                half3 col = _Tint.rgb * (0.7 + 0.3 * sky) + streak;
                half a = _BaseAlpha + _FresnelAlpha * fres + streak * 0.35 + edge * 0.55;
                // highlight (fish over the tank): pulsing golden rim on the glass
                float pulse = 0.6 + 0.4 * sin(_Time.y * 6.0);
                col = lerp(col, _HighlightColor.rgb, _Highlight * (edge * 0.9 + 0.15) * pulse);
                a += _Highlight * (edge * 0.4 + 0.08) * pulse;
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
