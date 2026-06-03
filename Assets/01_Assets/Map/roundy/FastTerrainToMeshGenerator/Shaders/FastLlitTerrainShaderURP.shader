Shader "Roundy/FastLitTerrainShaderURP"
{
    Properties
    {
        [Header(Main Properties)]
        [Space(15)]
        _SplatTex ("Splat Map", 2D) = "white" {}
        [Space(10)]
        
        [Header(Texture Properties)]
        [Space(15)]
        _MainTex0 ("Texture 0", 2D) = "white" {}
        _MainTex1 ("Texture 1", 2D) = "white" {}
        _MainTex2 ("Texture 2", 2D) = "white" {}
        _MainTex3 ("Texture 3", 2D) = "white" {}
        
        [Header(Normal Maps)]
        [Space(10)]
        // [ADDED] Toggle to enable/disable normal maps as a separate variant
        [Toggle(ENABLE_NORMAL_MAPS)] _EnableNormalMaps("Enable Normal Maps", Float) = 0
        
        [Normal] _BumpMap0 ("Normal 0", 2D) = "bump" {}
        [Normal] _BumpMap1 ("Normal 1", 2D) = "bump" {}
        [Normal] _BumpMap2 ("Normal 2", 2D) = "bump" {}
        [Normal] _BumpMap3 ("Normal 3", 2D) = "bump" {}
        
        [Header(Tint Colors)]
        [Space(10)]
        _TintColor0 ("Tint Color 0", Color) = (1,1,1,1)
        _TintColor1 ("Tint Color 1", Color) = (1,1,1,1)
        _TintColor2 ("Tint Color 2", Color) = (1,1,1,1)
        _TintColor3 ("Tint Color 3", Color) = (1,1,1,1)
        
        [Space(10)]
        [Header(Blending Properties)]
        [Space(15)]
        _HeightBlendDistance ("Height Blend Distance", Range(0.01, 1)) = 0.01
        _HeightBlendStrength ("Height Blend Strength", Range(0.01, 8)) = 1
        
        [Space(10)]
        [Header(Smoothness Properties)]
        [Space(15)]
        [Toggle(ENABLE_SMOOTHNESS)] _EnableSmoothnessFlag("Enable Alpha Channel Smoothness", Float) = 0
        _SmoothnessStrength("Smoothness Strength", Range(0, 2)) = 1.0

        [Header(Resampling Properties)]
        [Space(15)]
        [Toggle(ENABLE_RESAMPLING)] _EnableResampling("Enable Distance Resampling", Float) = 0
        _ResampleDistance ("Distance (Start, End)", Vector) = (20, 50, 0, 0)
        _ResampleTiling ("Tiling", Vector) = (0.2, 0.2, 0, 0)

        [HideInInspector] _Surface("__surface", Float) = 0.0
        [HideInInspector] _Blend("__blend", Float) = 0.0
        [HideInInspector] _Cull("__cull", Float) = 2.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
    }

    CustomEditor "FastLitTerrainShaderGUI"

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "SimpleLit"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex0_ST, _MainTex1_ST, _MainTex2_ST, _MainTex3_ST;
        float _HeightBlendDistance;
        float _HeightBlendStrength;
        float4 _TintColor0, _TintColor1, _TintColor2, _TintColor3;
        float _SmoothnessStrength;
        
        #ifdef ENABLE_RESAMPLING
            float4 _ResampleDistance;
            float4 _ResampleTiling;
        #endif
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend[_SrcBlend][_DstBlend]
            ZWrite[_ZWrite]
            Cull[_Cull]

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            // Material keywords
            #pragma shader_feature_local_fragment ENABLE_SMOOTHNESS
            #pragma shader_feature_local_fragment ENABLE_NORMAL_MAPS   // [ADDED]
            #pragma shader_feature_local ENABLE_RESAMPLING

            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

            struct Attributes
            {
                float4 positionOS    : POSITION;
                float3 normalOS      : NORMAL;
                float4 tangentOS     : TANGENT;
                float2 texcoord      : TEXCOORD0;
                float2 staticLightmapUV    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS               : SV_POSITION;
                float2 uv                       : TEXCOORD0;
                float3 positionWS               : TEXCOORD1;
                half3 normalWS                  : NORMAL;
                half4 tangentWS                 : TEXCOORD2;
                #ifdef ENABLE_RESAMPLING
                    float detailBlend          : TEXCOORD3;
                #endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
                half4 fogFactorAndVertexLight   : TEXCOORD5;
                float4 shadowCoord              : TEXCOORD6;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_SplatTex); SAMPLER(sampler_SplatTex);
            TEXTURE2D(_MainTex0); SAMPLER(sampler_MainTex0);
            TEXTURE2D(_MainTex1); SAMPLER(sampler_MainTex1);
            TEXTURE2D(_MainTex2); SAMPLER(sampler_MainTex2);
            TEXTURE2D(_MainTex3); SAMPLER(sampler_MainTex3);
            
            TEXTURE2D(_BumpMap0); SAMPLER(sampler_BumpMap0);
            TEXTURE2D(_BumpMap1); SAMPLER(sampler_BumpMap1);
            TEXTURE2D(_BumpMap2); SAMPLER(sampler_BumpMap2);
            TEXTURE2D(_BumpMap3); SAMPLER(sampler_BumpMap3);

            #define WEIGHT_THRESHOLD 0.01

            void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
            {
                inputData = (InputData)0;
                inputData.positionWS = input.positionWS;

                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float sgn = input.tangentWS.w;
                float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
                inputData.tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);

                inputData.normalWS = TransformTangentToWorld(normalTS, inputData.tangentToWorld);
                inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);

                inputData.viewDirectionWS = viewDirWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactorAndVertexLight.x;
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            }

            void InitializeSurfaceData(half4 albedo, half smoothness, half3 normalTS, out SurfaceData surfaceData)
            {
                surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo.rgb;
                surfaceData.alpha = albedo.a;
                surfaceData.metallic = 0;
                surfaceData.smoothness = smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.occlusion = 1;
                surfaceData.emission = 0;
            }

            void GetTextureAndHeight(
                float2 uv, 
                TEXTURE2D_PARAM(mainTex, samplerMainTex),
                TEXTURE2D_PARAM(bumpMap, samplerBumpMap),
                float4 mainTex_ST, float weight,
                #ifdef ENABLE_RESAMPLING
                    float detailBlend,
                #endif
                out half4 color, out half3 normal, out half height, out half smoothness
            )
            {
                if(weight < WEIGHT_THRESHOLD)
                {
                    color = half4(0,0,0,1);
                    normal = half3(0,0,1);
                    height = 0;
                    smoothness = 0;
                    return;
                }

                float2 scaledUV = uv * mainTex_ST.xy + mainTex_ST.zw;
                color = SAMPLE_TEXTURE2D(mainTex, samplerMainTex, scaledUV);

                // [ADDED] Wrap normal map sampling with #ifdef
                #ifdef ENABLE_NORMAL_MAPS
                    normal = UnpackNormal(SAMPLE_TEXTURE2D(bumpMap, samplerBumpMap, scaledUV));
                #else
                    normal = half3(0, 0, 1);
                #endif

                #ifdef ENABLE_RESAMPLING
                    half4 detailColor = SAMPLE_TEXTURE2D(mainTex, samplerMainTex, scaledUV * _ResampleTiling.xy);
                    color = lerp(color, detailColor, detailBlend);
                #endif

                height = color.r * weight;

                #ifdef ENABLE_SMOOTHNESS
                    smoothness = color.a * _SmoothnessStrength * weight;
                #else
                    smoothness = 0;
                #endif
            }

            Varyings LitPassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.uv = input.texcoord;
                output.normalWS = normalInput.normalWS;
                real sign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = half4(normalInput.tangentWS.xyz, sign);
                output.positionWS = vertexInput.positionWS;
                
                #ifdef ENABLE_RESAMPLING
                    float dist = distance(_WorldSpaceCameraPos, output.positionWS);
                    output.detailBlend = smoothstep(_ResampleDistance.x, _ResampleDistance.y, dist);
                #endif

                output.positionCS = vertexInput.positionCS;

                half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
                half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);

                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH(output.normalWS.xyz, output.vertexSH);

                output.shadowCoord = TransformWorldToShadowCoord(vertexInput.positionWS);

                return output;
            }

            half4 LitPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 splat = SAMPLE_TEXTURE2D(_SplatTex, sampler_SplatTex, input.uv);
                
                half4 weights = half4(
                    splat.r > WEIGHT_THRESHOLD ? splat.r : 0,
                    splat.g > WEIGHT_THRESHOLD ? splat.g : 0,
                    splat.b > WEIGHT_THRESHOLD ? splat.b : 0,
                    splat.a > WEIGHT_THRESHOLD ? splat.a : 0
                );
                
                half weightSum = weights.r + weights.g + weights.b + weights.a;
                if(weightSum > 0)
                {
                    weights /= weightSum;
                }

                half4 col0, col1, col2, col3;
                half3 norm0, norm1, norm2, norm3;
                half h0, h1, h2, h3;
                half s0, s1, s2, s3;

                #ifdef ENABLE_RESAMPLING
                    GetTextureAndHeight(input.uv, _MainTex0, sampler_MainTex0, 
                        _BumpMap0, sampler_BumpMap0, _MainTex0_ST, weights.r,
                        input.detailBlend, col0, norm0, h0, s0);
                    GetTextureAndHeight(input.uv, _MainTex1, sampler_MainTex1,
                        _BumpMap1, sampler_BumpMap1, _MainTex1_ST, weights.g,
                        input.detailBlend, col1, norm1, h1, s1);
                    GetTextureAndHeight(input.uv, _MainTex2, sampler_MainTex2,
                        _BumpMap2, sampler_BumpMap2, _MainTex2_ST, weights.b,
                        input.detailBlend, col2, norm2, h2, s2);
                    GetTextureAndHeight(input.uv, _MainTex3, sampler_MainTex3,
                        _BumpMap3, sampler_BumpMap3, _MainTex3_ST, weights.a,
                        input.detailBlend, col3, norm3, h3, s3);
                #else
                    GetTextureAndHeight(input.uv, _MainTex0, sampler_MainTex0,
                        _BumpMap0, sampler_BumpMap0, _MainTex0_ST, weights.r,
                        col0, norm0, h0, s0);
                    GetTextureAndHeight(input.uv, _MainTex1, sampler_MainTex1,
                        _BumpMap1, sampler_BumpMap1, _MainTex1_ST, weights.g,
                        col1, norm1, h1, s1);
                    GetTextureAndHeight(input.uv, _MainTex2, sampler_MainTex2,
                        _BumpMap2, sampler_BumpMap2, _MainTex2_ST, weights.b,
                        col2, norm2, h2, s2);
                    GetTextureAndHeight(input.uv, _MainTex3, sampler_MainTex3,
                        _BumpMap3, sampler_BumpMap3, _MainTex3_ST, weights.a,
                        col3, norm3, h3, s3);
                #endif

                // Calculate height blending
                half maxH = max(max(h0, h1), max(h2, h3));
                half4 heightBlend = saturate(1 - (maxH - half4(h0, h1, h2, h3)) / _HeightBlendDistance);
                heightBlend = pow(heightBlend, _HeightBlendStrength);
                heightBlend *= weights > WEIGHT_THRESHOLD;

                half heightSum = max(dot(heightBlend, half4(1,1,1,1)), 0.0001);
                heightBlend /= heightSum;

                // Apply tint colors
                col0.rgb *= _TintColor0.rgb;
                col1.rgb *= _TintColor1.rgb;
                col2.rgb *= _TintColor2.rgb;
                col3.rgb *= _TintColor3.rgb;

                // Blend colors and normals using height blend weights
                half4 albedo = 
                    col0 * heightBlend.r +
                    col1 * heightBlend.g +
                    col2 * heightBlend.b +
                    col3 * heightBlend.a;

                half3 normalTS = normalize(
                    norm0 * heightBlend.r +
                    norm1 * heightBlend.g +
                    norm2 * heightBlend.b +
                    norm3 * heightBlend.a
                );

                half smoothness = 
                    s0 * heightBlend.r +
                    s1 * heightBlend.g +
                    s2 * heightBlend.b +
                    s3 * heightBlend.a;

                // Initialize Surface and Input Data
                InputData inputData;
                InitializeInputData(input, normalTS, inputData);

                SurfaceData surfaceData;
                InitializeSurfaceData(albedo, smoothness, normalTS, surfaceData);

                // Calculate final lighting using URP's lighting
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);

                return color;
            }
            ENDHLSL
        }

        // Shadow casting support
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // Depth pass
        Pass
        {
            Name "DepthOnly"
            Tags{"LightMode" = "DepthOnly"}

            ZWrite On
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
    CustomEditor "FastTerrainShaderGUI"
}
