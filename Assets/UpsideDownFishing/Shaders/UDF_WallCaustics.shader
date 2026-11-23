// Dancing light from the ceiling sea projected onto the REAL walls (MR passthrough).
// We cannot relight the real room, but we can add light over it: additive caustic network,
// brightest just under the water and fading with distance below it.
// uv.x = metres along the wall, uv.y = metres below the water surface.
// Writes alpha as well as colour so it reads correctly whether the passthrough compositor
// treats the eye buffer as premultiplied or straight alpha.
Shader "UDF/WallCaustics"
{
    Properties
    {
        _Color ("Light Colour", Color) = (0.72,0.95,1,1)
        _Intensity ("Intensity", Float) = 0.6
        _Falloff ("Falloff (m)", Float) = 0.45
        _Scale ("Pattern Scale", Float) = 3.2
        _Glow ("Soft Glow Band", Float) = 0.08
        _AlphaWrite ("Alpha Contribution", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One, One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "UDF_Underwater.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity, _Falloff, _Scale, _Glow, _AlphaWrite;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float t = _Time.y;
                float d = max(i.uv.y, 0);
                float fade = exp(-d / max(_Falloff, 0.01)) * smoothstep(0.0, 0.05, d);
                // wobbling, rounded light patches (not hard Voronoi lines): the network is
                // domain-warped and softened, crisp right under the water, diffuse lower down
                float2 p = float2(i.uv.x, d * 0.7);
                p += 0.09 * float2(sin(p.y * 7.0 + t * 1.3), sin(p.x * 6.0 - t * 1.1));
                float c = UDF_Caustics(float3(p.x, 0, p.y), _Scale);
                float sharp = exp(-d / 0.25);
                c = smoothstep(lerp(0.35, 0.1, sharp), lerp(1.3, 0.9, sharp), c);
                float glow = _Glow * fade;
                float lum = (c * _Intensity * (0.45 + 0.55 * sharp) + glow) * fade * _UDF_WaterOn;
                return half4(_Color.rgb * lum, saturate(lum * _AlphaWrite));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
