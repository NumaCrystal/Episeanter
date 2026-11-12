Shader "Anapa/Pixel Water"
{
    Properties
    {
        _DeepColor ("Deep Color", Color) = (0.015, 0.18, 0.25, 1)
        _BaseColor ("Base Color", Color) = (0.025, 0.42, 0.5, 1)
        _WaveColor ("Wave Highlight", Color) = (0.2, 0.72, 0.72, 1)
        _EdgeColor ("Edge Color", Color) = (0.65, 0.95, 0.88, 1)
        _FoamColor ("Foam Color", Color) = (0.9, 1, 0.95, 1)
        _PixelDensity ("Pixel Density", Range(0.5, 32)) = 16
        _WaveFrequency ("Wave Frequency", Range(0.1, 8)) = 1.7
        _WaveSpeed ("Wave Speed", Range(0, 5)) = 0.7
        _WeatherSpeed ("Weather Speed", Range(0, 5)) = 1
        _WaveHighlight ("Wave Highlight Strength", Range(0, 1)) = 0.18
        _EdgePower ("Edge Falloff", Range(0.5, 8)) = 2.2
        _EdgeStrength ("Edge Brightness", Range(0, 2)) = 0.65
        _FoamThreshold ("Foam Threshold", Range(0, 1)) = 0.68
        _FoamWidth ("Foam Line Width", Range(0.005, 0.25)) = 0.06
        _FoamStrength ("Foam Strength", Range(0, 1)) = 0.8
        _FoamScale ("Foam Shape Scale", Range(0.1, 5)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "PixelWater"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _BaseColor;
                half4 _WaveColor;
                half4 _EdgeColor;
                half4 _FoamColor;
                float _PixelDensity;
                float _WaveFrequency;
                float _WaveSpeed;
                float _WeatherSpeed;
                float _WaveHighlight;
                float _EdgePower;
                float _EdgeStrength;
                float _FoamThreshold;
                float _FoamWidth;
                float _FoamStrength;
                float _FoamScale;
            CBUFFER_END

            float4 _RippleData[48];
            int _RippleCount;
            float4 _RippleSettings;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float Hash(float2 value)
            {
                return frac(sin(dot(value, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 position)
            {
                float2 cell = floor(position);
                float2 local = frac(position);
                local = local * local * (3.0 - 2.0 * local);
                float a = Hash(cell);
                float b = Hash(cell + float2(1.0, 0.0));
                float c = Hash(cell + float2(0.0, 1.0));
                float d = Hash(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, local.x), lerp(c, d, local.x), local.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float density = max(_PixelDensity, 0.01);
                float2 pixel = floor(input.positionWS.xz * density);
                float2 pixelWorld = (pixel + 0.5) / density;
                float noise = Hash(pixel);
                float weatherTime = _Time.y * max(_WeatherSpeed, 0.0);
                float2 wavePosition = pixelWorld * max(_WaveFrequency, 0.1);
                float waveTime = weatherTime * _WaveSpeed;
                float waveNoiseA = ValueNoise(wavePosition + float2(waveTime * 0.22, -waveTime * 0.13));
                float waveNoiseB = ValueNoise(wavePosition * 1.9 - float2(waveTime * 0.16, waveTime * 0.25));
                float waveNoiseC = ValueNoise(wavePosition * 3.7 + float2(waveTime * 0.31, -waveTime * 0.19));
                float wave = (waveNoiseA * 0.55 + waveNoiseB * 0.3 + waveNoiseC * 0.15) * 2.0 - 1.0;
                float rippleSignal = 0.0;
                float rippleFrontSpeed = max(_RippleSettings.x, 0.1);
                float rippleFrequency = max(_RippleSettings.y, 0.1);
                float rippleLifetime = max(_RippleSettings.z, 0.1);

                [loop]
                for (int i = 0; i < _RippleCount; i++)
                {
                    float4 ripple = _RippleData[i];
                    float age = _Time.y - ripple.z;
                    if (age < 0.0 || age >= rippleLifetime)
                        continue;

                    float distanceToSource = length(pixelWorld - ripple.xy);
                    float waveFront = age * rippleFrontSpeed;
                    float behindFront = smoothstep(0.0, 0.4, waveFront - distanceToSource);
                    float fade = 1.0 - age / rippleLifetime;
                    float attenuation = rcp(1.0 + distanceToSource * 0.12);
                    float rippleNoise = ValueNoise(float2(
                        distanceToSource * rippleFrequency - waveFront * rippleFrequency,
                        distanceToSource * 0.35 + age));
                    rippleSignal += (rippleNoise * 2.0 - 1.0)
                        * behindFront * fade * attenuation * ripple.w;
                }

                float wavePattern = wave + clamp(rippleSignal, -1.0, 1.0) * _RippleSettings.w;
                float variation = saturate(0.5 + wavePattern * 0.28 + (noise - 0.5) * 0.22);
                half3 color = lerp(_DeepColor.rgb, _BaseColor.rgb, variation);
                float sparkle = step(0.93, saturate(0.5 + wavePattern * 0.25 + noise * 0.25));
                color += _WaveColor.rgb * sparkle * _WaveHighlight;

                // World-space foam: layered noise creates broken islands and irregular lines.
                float2 foamPosition = pixelWorld * _FoamScale;
                float foamLarge = ValueNoise(foamPosition * 0.75 + float2(weatherTime * 0.04, -weatherTime * 0.025));
                float foamMedium = ValueNoise(foamPosition * 1.8 - float2(weatherTime * 0.08, weatherTime * 0.05));
                float foamDetail = ValueNoise(foamPosition * 4.5 + float2(weatherTime * 0.12, -weatherTime * 0.1));
                float foamField = saturate(
                    0.42 + wavePattern * 0.22
                    + foamLarge * 0.24
                    + foamMedium * 0.10
                    + foamDetail * 0.04
                    + abs(rippleSignal) * 0.35);
                float foamDistance = abs(foamField - _FoamThreshold);
                float foamLine = 1.0 - smoothstep(0.0, _FoamWidth, foamDistance);
                float foamIsland = smoothstep(
                    _FoamThreshold + _FoamWidth * 1.5,
                    _FoamThreshold + _FoamWidth * 5.0,
                    foamField) * 0.35;
                float foam = saturate(max(foamLine, foamIsland)) * _FoamStrength;
                color = lerp(color, _FoamColor.rgb, foam);

                half3 normalWS = normalize(input.normalWS);
                half3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirection)), _EdgePower);
                color += _EdgeColor.rgb * fresnel * _EdgeStrength;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
