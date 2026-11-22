Shader "HTownSim/WorldTriplanar"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1,1,1,1)
        _BaseMap("Detail Albedo (mid-grey = no change)", 2D) = "grey" {}
        _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1
        _TileSize("Tile Size (metres)", Float) = 4
        _DetailStrength("Detail Strength", Range(0,2)) = 1
        _Smoothness("Smoothness", Range(0,1)) = 0.3
        _Metallic("Metallic", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float _BumpScale;
                float _TileSize;
                float _DetailStrength;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
                UNITY_DOTS_INSTANCED_PROP(float, _BumpScale)
                UNITY_DOTS_INSTANCED_PROP(float, _TileSize)
                UNITY_DOTS_INSTANCED_PROP(float, _DetailStrength)
                UNITY_DOTS_INSTANCED_PROP(float, _Smoothness)
                UNITY_DOTS_INSTANCED_PROP(float, _Metallic)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #endif

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #ifdef UNITY_DOTS_INSTANCING_ENABLED
                float4 baseColor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
                float bumpScale = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _BumpScale);
                float tileSize = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _TileSize);
                float detailStrength = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _DetailStrength);
                float smoothness = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Smoothness);
                float metallic = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Metallic);
                #else
                float4 baseColor = _BaseColor; float bumpScale = _BumpScale; float tileSize = _TileSize;
                float detailStrength = _DetailStrength; float smoothness = _Smoothness; float metallic = _Metallic;
                #endif
                float3 n = normalize(i.normalWS);
                float3 w = pow(abs(n), 6);
                w /= (w.x + w.y + w.z);
                float3 pos = i.positionWS / tileSize;
                float2 uvX = pos.zy, uvY = pos.xz, uvZ = pos.xy;

                half3 albedoDetail = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * w.x
                                   + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * w.y
                                   + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * w.z;
                half3 detail = lerp(half3(1,1,1), albedoDetail * 2.0h, detailStrength);

                // Whiteout-blended triplanar normal mapping.
                half3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), bumpScale);
                half3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), bumpScale);
                half3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), bumpScale);
                tx = half3(tx.xy + n.zy, abs(tx.z) * n.x);
                ty = half3(ty.xy + n.xz, abs(ty.z) * n.y);
                tz = half3(tz.xy + n.xy, abs(tz.z) * n.z);
                float3 nW = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = nW;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(nW);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1,1,1,1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = saturate(baseColor.rgb * detail);
                s.metallic = metallic;
                s.smoothness = smoothness;
                s.occlusion = 1;
                s.alpha = 1;
                half4 c = UniversalFragmentPBR(inputData, s);
                c.rgb = MixFog(c.rgb, inputData.fogCoord);
                return c;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
