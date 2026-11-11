Shader "PSX/Lit"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,0)
        [NoScaleOffset] _EmissionMap ("Emission Map (uses Texture's tiling)", 2D) = "white" {}
        _AffineAmount ("Affine Texture Warp", Range(0,1)) = 1
        _SnapAmount ("Vertex Snapping", Range(0,1)) = 1
        _WetAmount ("Rain Wetness (shine + darkening)", Range(0,1)) = 0.4
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend (SrcAlpha for glass)", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend (OneMinusSrcAlpha for glass)", Float) = 0
        [Toggle] _ZWrite ("ZWrite (off for glass)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "PSXCommon.hlsl"

        TEXTURE2D(_EmissionMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;
            half4  color      : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uvPersp    : TEXCOORD0;
            noperspective float2 uvAffine : TEXCOORD1; // no perspective correction = PS1 texture warp
            float3 positionWS : TEXCOORD2;
            float3 normalWS   : TEXCOORD3;
            half4  color      : COLOR;
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = PSXSnapVertex(TransformWorldToHClip(o.positionWS));
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            float2 uv = TRANSFORM_TEX(v.uv, _BaseMap);
            o.uvPersp = uv;
            o.uvAffine = uv;
            o.color = v.color;
            return o;
        }

        float2 PSXUV(Varyings i)
        {
            return lerp(i.uvPersp, i.uvAffine, _AffineAmount);
        }

        half4 SampleAlbedo(Varyings i)
        {
            half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, PSXUV(i)) * _BaseColor * i.color;
            #if defined(_ALPHATEST_ON)
                clip(c.a - _Cutoff);
            #endif
            return c;
        }
        ENDHLSL

        Pass
        {
            Name "PSXForward"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ALPHATEST_ON

            half4 frag(Varyings i, bool isFront : SV_IsFrontFace) : SV_Target
            {
                half4 albedo = SampleAlbedo(i);
                float3 N = normalize(i.normalWS) * (isFront ? 1.0 : -1.0);

                // Rain: wet surfaces go darker and pick up glints, ground most of all.
                half wet = _PSXWeather.x * _WetAmount;
                albedo.rgb *= 1.0 - wet * 0.3;

                half3 light = _PSXAmbient.rgb + PSXFlashlight(i.positionWS, N) + PSXLamps(i.positionWS, N) + PSXLightning(N);
                half3 emission = _EmissionColor.rgb * SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, PSXUV(i)).rgb;
                half3 col = albedo.rgb * light + emission;
                col += PSXWetShine(i.positionWS, N, wet * lerp(0.25, 1.0, saturate(N.y)));

                col = PSXApplyFog(col, i.positionWS);
                col = PSXQuantize(col, i.positionCS.xy);
                return half4(col, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            #pragma shader_feature_local _ALPHATEST_ON

            half4 fragDepth(Varyings i) : SV_Target
            {
                SampleAlbedo(i);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepthNormals
            #pragma shader_feature_local _ALPHATEST_ON

            half4 fragDepthNormals(Varyings i) : SV_Target
            {
                SampleAlbedo(i);
                return half4(normalize(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
