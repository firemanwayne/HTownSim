Shader "HTownSim/SkyUnlit"
{
    Properties
    {
        _MainTex ("Sky Gradient", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        [Toggle] _ZWrite ("Z Write", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Background" }

        Pass
        {
            Name "SkyUnlit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 colour = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return half4(colour.rgb * _Tint.rgb, 1);
            }
            ENDHLSL
        }
    }
}
