Shader "PVFX/PVFX_URP_Lowest_CelShaderNotOutLine"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Base Texture (RGB)", 2D) = "white" {}

        [MaterialToggle] _UseShadeThreshold ("Use Shade Threshold", Float) = 0
        _ShadeThreshold ("Shade Threshold", Range(0,1)) = 0.5
        _ShadeIntensity ("Shade Intensity", Range(0,1)) = 0.5
        _ShadeColor ("Shade Color", Color) = (0,0,0,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "BASE"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            float _UseShadeThreshold;
            float _ShadeThreshold;
            float _ShadeIntensity;
            fixed4 _ShadeColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 texColor = tex2D(_MainTex, i.uv) * _Color;

                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float ndv = saturate(dot(normalize(i.worldNormal), viewDir));

                fixed toon = 1.0;
                if (_UseShadeThreshold > 0.5)
                {
                    toon = ndv > _ShadeThreshold ? 1.0 : (1.0 - _ShadeIntensity);
                }

                fixed4 finalColor = texColor * toon;

                if (_UseShadeThreshold > 0.5 && ndv <= _ShadeThreshold)
                {
                    finalColor.rgb = lerp(finalColor.rgb, _ShadeColor.rgb, _ShadeIntensity);
                }

                finalColor.a = 1.0;
                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
