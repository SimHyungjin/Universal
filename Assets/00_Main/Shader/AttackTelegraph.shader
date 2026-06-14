Shader "Universal/Attack Telegraph"
{
    Properties
    {
        _TelegraphColor ("Color", Color) = (1, 0, 0, 1)
        _Progress ("Progress", Range(0, 1)) = 0
        _ShapeType ("Shape Type", Float) = 0
        _ShapeParams ("Shape Params", Vector) = (1, 1.570796, 1, 1)
        _HitboxOffset ("Hitbox Offset", Float) = 0
        _TelegraphSize ("Telegraph Size", Float) = 4
        _FillRange ("Fill Range", Float) = 1
        _EdgeWidth ("Edge Width", Float) = 0.08
        _Opacity ("Opacity", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "AttackTelegraph"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TelegraphColor;
                float _Progress;
                float _ShapeType;
                float4 _ShapeParams;
                float _HitboxOffset;
                float _TelegraphSize;
                float _FillRange;
                float _EdgeWidth;
                float _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float BoxSdf(float2 p, float2 center, float2 halfExtents)
            {
                float2 q = abs(p - center) - halfExtents;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
            }

            void SphereMask(float2 p, out float inside, out float edgeDistance)
            {
                float radius = max(_ShapeParams.x, 0.0001);
                float dist = length(p - float2(0.0, _HitboxOffset)) - radius;
                float aa = max(fwidth(dist), 0.002);
                inside = 1.0 - smoothstep(0.0, aa, dist);
                edgeDistance = abs(dist);
            }

            void BoxMask(float2 p, out float inside, out float edgeDistance)
            {
                float lengthValue = max(_ShapeParams.z, 0.0001);
                float widthValue = max(_ShapeParams.w, 0.0001);
                float2 center = float2(0.0, _HitboxOffset + lengthValue * 0.5);
                float dist = BoxSdf(p, center, float2(widthValue * 0.5, lengthValue * 0.5));
                float aa = max(fwidth(dist), 0.002);
                inside = 1.0 - smoothstep(0.0, aa, dist);
                edgeDistance = abs(dist);
            }

            void ConeMask(float2 p, out float inside, out float edgeDistance)
            {
                float2 delta = p - float2(0.0, _HitboxOffset);
                float dist = length(delta);
                float lengthValue = max(max(_ShapeParams.x, _ShapeParams.z), 0.0001);
                float angle = clamp(_ShapeParams.y, 0.0174533, 6.2831853);
                float halfAngle = angle * 0.5;

                float radialInside = 1.0 - smoothstep(lengthValue, lengthValue + max(fwidth(dist), 0.002), dist);
                float sideInside = 1.0;
                float sideDistance = 999.0;

                if (angle < 6.281)
                {
                    float currentAngle = abs(atan2(delta.x, delta.y));
                    float angleDelta = halfAngle - currentAngle;
                    float angleAa = max(fwidth(currentAngle), 0.001);
                    sideInside = smoothstep(-angleAa, angleAa, angleDelta);
                    sideDistance = abs(sin(angleDelta) * dist);
                }

                inside = radialInside * sideInside;
                edgeDistance = min(abs(lengthValue - dist), sideDistance);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 p = (input.uv - 0.5) * _TelegraphSize;

                float inside;
                float edgeDistance;
                if (_ShapeType > 1.5)
                {
                    BoxMask(p, inside, edgeDistance);
                }
                else if (_ShapeType > 0.5)
                {
                    ConeMask(p, inside, edgeDistance);
                }
                else
                {
                    SphereMask(p, inside, edgeDistance);
                }

                clip(inside - 0.001);

                float fillDistance = length(p);
                float fillMask = 1.0 - smoothstep(_Progress * _FillRange, _Progress * _FillRange + max(fwidth(fillDistance), 0.002), fillDistance);
                float edgeMask = 1.0 - smoothstep(_EdgeWidth, _EdgeWidth + max(fwidth(edgeDistance), 0.002), edgeDistance);

                float brightness = lerp(0.22, 0.85, fillMask);
                brightness = lerp(brightness, 1.0, edgeMask);

                half alpha = (half)(inside * _Opacity * lerp(0.45, 1.0, max(fillMask, edgeMask)));
                return half4(_TelegraphColor.rgb * brightness, alpha);
            }
            ENDHLSL
        }
    }
}
