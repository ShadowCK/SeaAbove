// Soap-film / air bubble billboard for particles: thin bright rim with a hint of
// iridescence, two specular highlights, almost clear centre. Premultiplied alpha.
Shader "UDF/Bubble"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.9,1,1,1)
        _Iridescence ("Iridescence", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Iridescence;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 q = i.uv * 2 - 1;
                float r = length(q);
                if (r > 1) return 0;
                float rim = smoothstep(0.62, 0.97, r) * (1 - smoothstep(0.97, 1.0, r));
                float spot = smoothstep(0.26, 0.0, length(q - float2(-0.36, 0.4)));
                float spot2 = smoothstep(0.12, 0.0, length(q - float2(0.32, -0.38))) * 0.45;
                half3 irid = 0.55 + 0.45 * cos(6.2831 * (r * 1.4 + q.x * 0.3 + float3(0.0, 0.33, 0.67) + _Time.y * 0.05));
                half3 rimCol = lerp(_Tint.rgb, irid, _Iridescence);
                half a = saturate(rim * 0.65 + spot * 0.9 + spot2 + 0.04);
                half3 col = rimCol * rim * 0.85 + (spot + spot2) * half3(1, 1, 1) + _Tint.rgb * 0.03;
                return half4(col, a) * i.color.a * half4(i.color.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
