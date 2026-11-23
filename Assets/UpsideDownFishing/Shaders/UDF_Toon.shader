// Stylized cartoon shading for fish, rod and decorations.
// Two-tone ramp + rim light, vertex colours, and an optional swim wiggle along local +Z.
Shader "UDF/Toon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.55,0.6,0.85,1)
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimPower ("Rim Power", Range(0.5,8)) = 3
        _Emission ("Emission", Color) = (0,0,0,0)
        _RampSmooth ("Ramp Smoothness", Range(0.001,0.5)) = 0.08
        _SwimAmp ("Swim Amplitude", Float) = 0
        _SwimFreq ("Swim Frequency (per m)", Float) = 18
        _SwimSpeed ("Swim Speed", Float) = 8
        _SwimPhase ("Swim Phase", Float) = 0
        _HeadZ ("Head Z (no wiggle in front)", Float) = 0.05
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _ShadeColor, _RimColor, _Emission;
                half _RimPower, _RampSmooth;
                float _SwimAmp, _SwimFreq, _SwimSpeed, _SwimPhase, _HeadZ;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; half4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = v.positionOS.xyz;
                // wiggle grows toward the tail (negative z)
                float tail = saturate((_HeadZ - p.z) * 4.0);
                p.x += sin(_Time.y * _SwimSpeed + _SwimPhase - p.z * _SwimFreq) * _SwimAmp * tail;
                VertexPositionInputs pos = GetVertexPositionInputs(p);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light light = GetMainLight();
                half ndl = dot(n, light.direction) * 0.5h + 0.5h;
                half ramp = smoothstep(0.5h - _RampSmooth, 0.5h + _RampSmooth, ndl);
                half3 albedo = _BaseColor.rgb * i.color.rgb;
                half3 col = lerp(albedo * _ShadeColor.rgb, albedo, ramp);
                col += SampleSH(n) * albedo * 0.25h;
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0h - saturate(dot(n, v)), _RimPower);
                col += _RimColor.rgb * rim * 0.6h;
                col += _Emission.rgb;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
