Shader "Roundy/FastLitTerrainShaderBIRP"
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
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        
        #pragma shader_feature_local_fragment ENABLE_SMOOTHNESS
        #pragma shader_feature_local_fragment ENABLE_NORMAL_MAPS
        #pragma shader_feature_local ENABLE_RESAMPLING

        #define WEIGHT_THRESHOLD 0.01

        sampler2D _SplatTex;
        
        sampler2D _MainTex0;
        sampler2D _MainTex1;
        sampler2D _MainTex2;
        sampler2D _MainTex3;
        
        sampler2D _BumpMap0;
        sampler2D _BumpMap1;
        sampler2D _BumpMap2;
        sampler2D _BumpMap3;

        float4 _MainTex0_ST;
        float4 _MainTex1_ST;
        float4 _MainTex2_ST;
        float4 _MainTex3_ST;

        fixed4 _TintColor0;
        fixed4 _TintColor1;
        fixed4 _TintColor2;
        fixed4 _TintColor3;

        float _HeightBlendDistance;
        float _HeightBlendStrength;
        float _SmoothnessStrength;

        #ifdef ENABLE_RESAMPLING
            float4 _ResampleDistance;
            float4 _ResampleTiling;
        #endif

        struct Input
        {
            float2 uv_SplatTex;
            float3 worldPos;
        };

        void GetTextureAndHeight(
            float2 uv,
            sampler2D mainTex,
            sampler2D bumpMap,
            float4 mainTexST,
            float weight,
            #ifdef ENABLE_RESAMPLING
                float detailBlend,
            #endif
            out fixed4 color,
            out fixed3 normal,
            out half height,
            out half smoothness
        )
        {
            if(weight < WEIGHT_THRESHOLD)
            {
                color = fixed4(0,0,0,1);
                normal = fixed3(0,0,1);
                height = 0;
                smoothness = 0;
                return;
            }

            float2 scaledUV = uv * mainTexST.xy + mainTexST.zw;
            color = tex2D(mainTex, scaledUV);

            #ifdef ENABLE_NORMAL_MAPS
                normal = UnpackNormal(tex2D(bumpMap, scaledUV));
            #else
                normal = fixed3(0, 0, 1);
            #endif

            #ifdef ENABLE_RESAMPLING
                fixed4 detailColor = tex2D(mainTex, scaledUV * _ResampleTiling.xy);
                color = lerp(color, detailColor, detailBlend);
            #endif

            height = color.r * weight;

            #ifdef ENABLE_SMOOTHNESS
                smoothness = color.a * _SmoothnessStrength * weight;
            #else
                smoothness = 0;
            #endif
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 splat = tex2D(_SplatTex, IN.uv_SplatTex);

            fixed4 weights = fixed4(
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

            fixed4 col0, col1, col2, col3;
            fixed3 norm0, norm1, norm2, norm3;
            half h0, h1, h2, h3;
            half s0, s1, s2, s3;

            #ifdef ENABLE_RESAMPLING
                float dist = distance(_WorldSpaceCameraPos, IN.worldPos);
                float detailBlend = smoothstep(_ResampleDistance.x, _ResampleDistance.y, dist);

                GetTextureAndHeight(IN.uv_SplatTex, _MainTex0, _BumpMap0, _MainTex0_ST, weights.r, 
                    detailBlend, col0, norm0, h0, s0);
                GetTextureAndHeight(IN.uv_SplatTex, _MainTex1, _BumpMap1, _MainTex1_ST, weights.g,
                    detailBlend, col1, norm1, h1, s1);
                GetTextureAndHeight(IN.uv_SplatTex, _MainTex2, _BumpMap2, _MainTex2_ST, weights.b,
                    detailBlend, col2, norm2, h2, s2);
                GetTextureAndHeight(IN.uv_SplatTex, _MainTex3, _BumpMap3, _MainTex3_ST, weights.a,
                    detailBlend, col3, norm3, h3, s3);
            #else
                GetTextureAndHeight(IN.uv_SplatTex, _MainTex0, _BumpMap0, _MainTex0_ST, weights.r,
                    col0, norm0, h0, s0);
                GetTextureAndHeight(IN.uv_SplatTex, _MainTex1, _BumpMap1, _MainTex1_ST, weights.g,
                    col1, norm1, h1, s1);
                GetTextureAndHeight(IN.uv_SplatTex, _MainTex2, _BumpMap2, _MainTex2_ST, weights.b,
                    col2, norm2, h2, s2);
                GetTextureAndHeight(IN.uv_SplatTex, _MainTex3, _BumpMap3, _MainTex3_ST, weights.a,
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
            o.Albedo = 
                col0.rgb * heightBlend.r +
                col1.rgb * heightBlend.g +
                col2.rgb * heightBlend.b +
                col3.rgb * heightBlend.a;

            o.Normal = normalize(
                norm0 * heightBlend.r +
                norm1 * heightBlend.g +
                norm2 * heightBlend.b +
                norm3 * heightBlend.a
            );

            o.Smoothness = 
                s0 * heightBlend.r +
                s1 * heightBlend.g +
                s2 * heightBlend.b +
                s3 * heightBlend.a;

            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
    CustomEditor "FastTerrainShaderGUI"
}