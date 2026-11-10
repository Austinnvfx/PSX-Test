// Inside-out sphere around the camera that paints the fog "sky", including the lamp and flashlight
// glow, wherever no geometry was drawn. Without it, halos would vanish against the empty background.
Shader "PSX/FogSky"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+400" }

        Pass
        {
            Name "PSXFogSky"
            Tags { "LightMode"="UniversalForward" }
            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "PSXCommon.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 col = PSXApplyFog(_PSXFogColor.rgb, i.positionWS);
                col = PSXQuantize(col, i.positionCS.xy);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
