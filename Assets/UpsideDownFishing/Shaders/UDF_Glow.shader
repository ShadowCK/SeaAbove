// Unlit, alpha-blended colour (vertex colour * tint). Used for particles, fishing line,
// bubbles, UI bars and glow halos.
Shader "UDF/Glow"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Soft ("Round Soft Particle", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
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
                half4 _BaseColor;
                half _Soft;
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
                o.color = v.color * _BaseColor;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 c = i.color;
                if (_Soft > 0)
                {
                    float d = length(i.uv - 0.5) * 2;
                    // bubble: bright rim + soft centre
                    half a = smoothstep(1.0, 0.8, d) * (0.35 + 0.65 * smoothstep(0.5, 0.95, d));
                    c.a *= lerp(1, a, _Soft);
                }
                return c;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
