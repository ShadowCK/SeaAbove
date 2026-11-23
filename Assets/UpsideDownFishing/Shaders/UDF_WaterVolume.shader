// Interior of the virtual sea above the ceiling. Rendered with front-face culling so the
// player, standing below the surface, sees the far walls and the (upside-down) sea "floor".
// Colour deepens with height above the surface; soft god-ray stripes and caustics.
Shader "UDF/WaterVolume"
{
    Properties
    {
        _TopColor ("Deep (far) Color", Color) = (0.05,0.12,0.38,1)
        _BottomColor ("Near-surface Color", Color) = (0.15,0.6,0.85,1)
        _RayColor ("Light Ray Color", Color) = (0.6,1,1,1)
        _SurfaceY ("Surface Y", Float) = 2.6
        _Depth ("Depth", Float) = 1.6
        _Alpha ("Alpha", Range(0,1)) = 0.92
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
                half4 _TopColor, _BottomColor, _RayColor;
                float _SurfaceY, _Depth, _Alpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float h = saturate((i.positionWS.y - _SurfaceY) / max(_Depth, 0.01));
                half3 col = lerp(_BottomColor.rgb, _TopColor.rgb, smoothstep(0, 1, h));
                float t = _Time.y;
                float rays = sin((i.positionWS.x + i.positionWS.z) * 3.0 + t * 0.6) * sin(i.positionWS.x * 1.7 - t * 0.4);
                rays = smoothstep(0.55, 1.0, rays) * (1 - h);
                col += _RayColor.rgb * rays * 0.25;
                return half4(col, _Alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
