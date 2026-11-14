Shader "Custom/PixelWaterVoronoi"
{
    Properties
    {
        [Header(Colors)]
        _DeepColor    ("Deep Color",    Color) = (0.015, 0.18, 0.25, 1)
        _ShallowColor ("Base Color",    Color) = (0.025, 0.42, 0.50, 1)
        _FoamColor    ("Foam Color",    Color) = (0.90, 1.00, 0.95, 1)
        _ObjectFoamColor ("Object Foam Color", Color) = (0.90, 1.00, 0.95, 1)
        _Alpha        ("Alpha", Range(0, 1)) = 0.9
        _IntersectionFoamWidth ("Object Foam Width", Range(0.001, 1)) = 0.12
        _IntersectionFoamDepth ("Object Foam Depth", Range(0, 1)) = 0.08
        _IntersectionFoamStrength ("Object Foam Strength", Range(0, 1)) = 1

        [Header(Pixelation)]
        _PixelDensity ("Pixels Per Unit", Float) = 16
        _Steps        ("Color Steps (posterize)", Range(2, 8)) = 4

        [Header(Voronoi)]
        _Scale        ("Cell Scale", Float) = 1.5
        _FlowDirection ("Flow Direction (X/Z or X/Y)", Vector) = (1, 0, 0, 0)
        _FlowSpeed    ("Flow Speed", Float) = 0.4
        _EdgeWidth    ("Edge Width", Range(0.0, 0.5)) = 0.08
        _Distortion   ("Warp Strength", Range(0, 1)) = 0.35
        _WarpScale    ("Warp Scale", Float) = 0.8

        [Header(Orientation)]
        [KeywordEnum(XZ_3D_Plane, XY_2D_Sprite)] _Axis ("World Axis", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "PixelWater"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _AXIS_XZ_3D_PLANE _AXIS_XY_2D_SPRITE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _DeepColor;
                half4  _ShallowColor;
                half4  _FoamColor;
                half4  _ObjectFoamColor;
                half   _Alpha;
                float  _IntersectionFoamWidth;
                float  _IntersectionFoamDepth;
                float  _IntersectionFoamStrength;
                float  _PixelDensity;
                float  _Steps;
                float  _Scale;
                float4 _FlowDirection;
                float  _FlowSpeed;
                float  _EdgeWidth;
                float  _Distortion;
                float  _WarpScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            // Случайная точка для ячейки
            float2 hash22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Возвращает: x = расстояние до ближайшей точки (F1),
            //             y = F2 - F1 (расстояние до границы ячейки)
            float2 voronoi(float2 uv, float time)
            {
                float2 cell  = floor(uv);
                float2 local = frac(uv);

                float f1 = 8.0;
                float f2 = 8.0;

                [unroll]
                for (int j = -1; j <= 1; j++)
                {
                    [unroll]
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 g = float2(i, j);
                        float2 o = hash22(cell + g);
                        // анимируем точку по кругу
                        o = 0.5 + 0.5 * sin(time + 6.2831853 * o);

                        float2 d = g + o - local;
                        float dist = dot(d, d);

                        if (dist < f1)      { f2 = f1; f1 = dist; }
                        else if (dist < f2) { f2 = dist; }
                    }
                }
                f1 = sqrt(f1);
                f2 = sqrt(f2);
                return float2(f1, f2 - f1);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 1. Мировые координаты плоскости воды
                #if defined(_AXIS_XY_2D_SPRITE)
                    float2 p = IN.positionWS.xy;
                #else
                    float2 p = IN.positionWS.xz;
                #endif

                // 2. Пикселизация: привязываем координаты к сетке
                p = floor(p * _PixelDensity) / _PixelDensity;

                float2 flowDirection = _FlowDirection.xy;
                flowDirection /= max(length(flowDirection), 0.001);
                float flowTime = _Time.y * _FlowSpeed;
                float2 flowOffset = flowDirection * flowTime;

                // 3. Слой искажения (warp) — делает воду "живой"
                float2 warp = voronoi(p * _WarpScale + flowOffset * 0.7 + 17.3, flowTime * 0.7).xx;
                float2 uv = p * _Scale + flowOffset + (warp - 0.5) * _Distortion;

                // 4. Основной Вороной
                float2 v = voronoi(uv, flowTime);
                float cells = v.x;   // 0 в центре ячейки, ~1 у края
                float edge  = v.y;   // 0 на границе ячеек

                // 5. Постеризация цвета воды
                float shade = floor(saturate(cells) * _Steps) / (_Steps - 1.0);
                shade = saturate(shade);
                half3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, shade);

                // 6. Каустика / пена на границах ячеек (жёсткий пиксельный край)
                float edgeLine = step(edge, _EdgeWidth);
                col = lerp(col, _FoamColor.rgb, edgeLine);

                // 7. Редкие блики в центрах ячеек
                float sparkle = step(cells, 0.07) * step(0.5, frac(sin(dot(floor(uv), float2(12.9898, 78.233))) * 43758.5453));
                col = lerp(col, _FoamColor.rgb, sparkle);

                return half4(col, _Alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ObjectIntersectionFoam"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            Stencil
            {
                Ref 1
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex FoamVert
            #pragma fragment FoamFrag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ObjectFoamColor;
                float _IntersectionFoamWidth;
                float _IntersectionFoamDepth;
                float _IntersectionFoamStrength;
            CBUFFER_END

            struct FoamAttributes
            {
                float4 positionOS : POSITION;
            };

            struct FoamVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            FoamVaryings FoamVert(FoamAttributes input)
            {
                FoamVaryings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 FoamFrag(FoamVaryings input) : SV_Target
            {
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float sceneDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(sceneDepth, _ZBufferParams);
                float waterEyeDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float objectDepth = waterEyeDepth - sceneEyeDepth;
                float objectInFront = step(0.0, objectDepth);
                float intersectionFoam = 1.0 - smoothstep(
                    _IntersectionFoamDepth,
                    _IntersectionFoamDepth + _IntersectionFoamWidth,
                    objectDepth);
                intersectionFoam *= objectInFront * _IntersectionFoamStrength;

                return half4(_ObjectFoamColor.rgb, saturate(intersectionFoam));
            }
            ENDHLSL
        }
    }
}
