// Opaque surfaces living inside the upside-down sea: scanned rocks/corals (albedo + normal
// map) and procedural kelp (vertex colour, double-sided, current sway).
// Lighting/fog/caustics come from UDF_Underwater.hlsl so everything in the sea matches.
Shader "UDF/Underwater"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1
        _Smoothness ("Wetness", Range(0,1)) = 0.35
        _Caustics ("Caustic Strength", Range(0,2)) = 1
        _UseVertexColor ("Use Vertex Colour", Range(0,1)) = 0
        _Translucency ("Translucency (thin blades)", Range(0,1)) = 0
        _Sway ("Sway Amount (m at uv.y=1)", Float) = 0
        _SwayFreq ("Sway Speed", Float) = 1.3
        _Emission ("Emission", Color) = (0,0,0,0)
        _RimLight ("Rim Light", Range(0,1)) = 0
        _SwimAmp ("Swim Amplitude", Float) = 0
        _SwimFreq ("Swim Frequency (per m)", Float) = 18
        _SwimSpeed ("Swim Speed", Float) = 8
        _SwimPhase ("Swim Phase", Float) = 0
        _HeadZ ("Head Z (no wiggle in front)", Float) = 0.05
        _TankLit ("Aquarium Lighting (0 sea, 1 lamp above, 2 upside-down tank lit from below)", Range(0,2)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "UDF_Underwater.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale, _Smoothness, _Caustics, _UseVertexColor, _Translucency;
                float _Sway, _SwayFreq, _Cull;
                half4 _Emission;
                half _RimLight, _TankLit;
                float _SwimAmp, _SwimFreq, _SwimSpeed, _SwimPhase, _HeadZ;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = v.positionOS.xyz;
                if (_SwimAmp > 0)
                {
                    // body wave grows toward the tail (head is +Z)
                    float tail = saturate((_HeadZ - p.z) * 4.0);
                    p.x += sin(_Time.y * _SwimSpeed + _SwimPhase - p.z * _SwimFreq) * _SwimAmp * tail;
                }
                float3 ws = TransformObjectToWorld(p);
                if (_Sway > 0)
                {
                    // slow current + faster flutter, growing quadratically toward the free end
                    float w = v.uv.y * v.uv.y * _Sway;
                    float t = _Time.y * _SwayFreq;
                    float ph = dot(ws.xz, float2(1.3, 0.9));
                    ws.x += (sin(t + ph) * 0.8 + sin(t * 2.7 + ph * 2.1) * 0.25) * w;
                    ws.z += (cos(t * 0.8 + ph * 1.2) * 0.6 + sin(t * 3.1 + ph) * 0.2) * w;
                }
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w * GetOddNegativeScale());
                o.color = v.color;
                return o;
            }

            half4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb * lerp(half3(1, 1, 1), i.color.rgb, _UseVertexColor);

                float3 n = normalize(i.normalWS);
                float3 tng = i.tangentWS.xyz;
                if (dot(tng, tng) > 1e-4)
                {
                    half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                    tng = normalize(tng - n * dot(tng, n));
                    float3 b = cross(n, tng) * (i.tangentWS.w > 0 ? 1.0 : -1.0);
                    n = normalize(tng * nTS.x + b * nTS.y + n * nTS.z);
                }
                if (!front) n = -n;

                // in the sea the light comes through the surface below; in the aquarium from the lamp above
                float tankUp = _TankLit > 1.5 ? -1.0 : 1.0;
                half3 col = _TankLit > 0.5 ? UDF_TankLighting(albedo, n, i.positionWS, _Smoothness, _Caustics, tankUp)
                                           : UDF_SeaLighting(albedo, n, i.positionWS, _Smoothness, _Caustics);
                // thin blades let light through: the shaded side still glows with the blade colour
                float3 L = _TankLit > 0.5 ? float3(0, tankUp, 0) : normalize(_UDF_SeaLightDir.xyz);
                col += albedo * _UDF_SeaLightColor.rgb * _Translucency * saturate(-dot(n, L) + 0.2) * 0.6;
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                col += _RimLight * pow(1.0 - saturate(dot(n, V)), 3.0) * half3(0.8, 0.95, 1.0);
                col += _Emission.rgb;
                col = UDF_ApplyWaterFog(col, i.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
