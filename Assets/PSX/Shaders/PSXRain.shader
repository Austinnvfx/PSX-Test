// Rain streaks and splashes (particles). Lit by the street lamps, flashlight and lightning,
// so drops glitter as they fall through a lamp's light. Fades out with distance like the haze.
Shader "PSX/Rain"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (0.55, 0.6, 0.7, 0.35)
        _FadeDistance ("Fade Distance", Float) = 28
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "PSXRain"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PSXCommon.hlsl"

            float _FadeDistance;

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4  color      : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);

                // Drops catch light from every nearby lamp regardless of facing.
                half3 light = _PSXAmbient.rgb * 1.5 + _PSXWeather.y * half3(0.8, 0.85, 1.0);
                int count = (int)_PSXLampCount;
                for (int l = 0; l < PSX_MAX_LAMPS; l++)
                {
                    if (l >= count) break;
                    float d = distance(i.positionWS, _PSXLampPos[l].xyz);
                    float atten = saturate(1.0 - d / _PSXLampPos[l].w);
                    light += _PSXLampColor[l].rgb * atten * atten * 1.5;
                }
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                light += PSXFlashlight(i.positionWS, V);

                half3 col = _BaseColor.rgb * i.color.rgb * light;
                float dist = distance(i.positionWS, _WorldSpaceCameraPos);
                half a = tex.a * i.color.a * _BaseColor.a * saturate(1.0 - dist / _FadeDistance);

                col = PSXQuantize(col, i.positionCS.xy);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
