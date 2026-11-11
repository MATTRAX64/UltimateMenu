// UltimateMenu - fog + rayons de lumiere (god rays) en espace ecran, URP, compatible VR.
// Les rayons partent du soleil : tout mesh qui cache le ciel les bloque (aucune shadow map).
// Bundle : Tools > Volumetric Fog Bundle (ou Unity -batchmode ... -executeMethod VolumetricFogBundleWindow.BuildBatch).
// VR : l'editeur retire STEREO_INSTANCING_ON des AssetBundles, donc le mot-cle maison UM_STEREO active le mode
// single pass instanced ; GorillaShader.cs l'allume uniquement pour les cameras XR (Camera.stereoEnabled).
Shader "UltimateMenu/VolumetricFog"
{
    Properties
    {
        _FogColor ("Fog color", Color) = (0.85, 0.65, 0.42, 1)
        _Density ("Fog density", Float) = 0.02
        _Scattering ("Light scattering", Float) = 0.35
        _Anisotropy ("Anisotropy", Range(0, 0.95)) = 0.6
        _Ambient ("Ambient fog", Range(0, 1)) = 0.15
        _MaxDistance ("Max distance", Float) = 80
        _Steps ("Ray steps", Float) = 32
        _SunDir ("Direction towards the sun (world)", Vector) = (0.3, 0.6, 0.5, 0)
        _SunColor ("Sun color", Color) = (1, 0.72, 0.4, 1)
        _SunRadius ("Sun angular radius (radians)", Float) = 0.0192
        _SunIntensity ("Sun intensity", Float) = 1.5
        _Outdoor ("Outdoor factor (0 = inside, 1 = outside)", Range(0, 1)) = 1
        _SkyDistance ("Distance beyond which a mesh counts as sky", Float) = 2000
        _ShaftFocus ("Light concentration around the sun", Float) = 6
        _StreakCount ("Number of light streaks", Float) = 48
        _StreakStrength ("Streak contrast", Range(0, 1)) = 0.6
        _DebugMask ("Debug: show ray occlusion in red", Float) = 0
        _SkyThreshold ("Fraction of far plane counted as sky", Range(0.5, 1)) = 0.999
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Volumetric"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            // Variante VR maison : l'editeur retire STEREO_INSTANCING_ON des AssetBundles (stripping), donc le mode
            // single pass instanced est active par ce mot-cle, positionne par camera depuis GorillaShader.cs.
            #pragma multi_compile_local _ UM_STEREO
            #if defined(UM_STEREO) && !defined(STEREO_INSTANCING_ON)
                #define STEREO_INSTANCING_ON 1
            #endif

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float _Density;
                float _Scattering;
                float _Anisotropy;
                float _Ambient;
                float _MaxDistance;
                float _Steps;
                float4 _SunDir;
                float4 _SunColor;
                float _SunRadius;
                float _SunIntensity;
                float _Outdoor;
                float _SkyDistance;
                float _ShaftFocus;
                float _StreakCount;
                float _StreakStrength;
                float _DebugMask;
                float _SkyThreshold;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // L'UV (0..1) du quad sert de position clip : le quad couvre tout l'écran de chaque oeil / caméra.
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = float4(input.uv * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
                return output;
            }

            // Profondeur (en mètres) de la surface visible ; LOD 0 pour pouvoir l'appeler dans une boucle.
            float SceneEyeDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            float Hash11(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            // 1 / cos(angle entre le rayon du pixel et l'axe de la camera) : eyeDepth * RadialFactor = distance reelle camera -> surface.
            float RadialFactor(float2 uv)
            {
                float3 nearPos = ComputeWorldSpacePosition(uv, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 dir = normalize(nearPos - GetCameraPositionWS());
                float3 fwd = normalize(-UNITY_MATRIX_V[2].xyz);
                return 1.0 / max(dot(dir, fwd), 0.2);
            }

            // 1 = on voit du ciel / très loin (la lumière passe), 0 = un mesh bloque.
            float LightMask(float eyeDepth, float2 uv)
            {
                float far = _ProjectionParams.z;
                if (eyeDepth >= far * _SkyThreshold) return 1.0;
                if (eyeDepth < _SkyDistance * 0.18) return 0.0;
                float radial = RadialFactor(uv);
                // Au-dela de _SkyDistance (ex. 2000 m : dome du ciel, montagnes tres lointaines) on force : la lumiere passe quand meme.
                // radial = 1/cos(angle avec l'axe de la camera) : la depth est mesuree selon l'axe, la distance du ciel est une vraie distance.
                float distant = smoothstep(_SkyDistance * 0.9, _SkyDistance, eyeDepth * radial);
                return distant;
            }

            // Bruit 1D en fonction de l'angle autour du soleil : donne des faisceaux distincts (clair / sombre) qui partent du soleil,
            // meme quand rien dans la scene ne les decoupe. Boucle sans couture a +-PI.
            float Streaks(float ang)
            {
                float count = max(1.0, round(_StreakCount));
                float a = (ang / (2.0 * PI) + 0.5) * count;
                float i = floor(a);
                float f = frac(a);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(Hash11(fmod(i, count)), Hash11(fmod(i + 1.0, count)), f);
            }

            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5));
            }

            float2 DirectionUV(float4 positionCS)
            {
                float2 uv = positionCS.xy / max(positionCS.w, 1e-4) * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1.0 - uv.y;
                #endif
                return uv;
            }

            float SourceTap(float4 positionCS)
            {
                if (positionCS.w <= 0.0) return 0.0;
                float2 uv = DirectionUV(positionCS);
                // Use the nearest visible depth as a proxy for the small off-screen tail.
                float2 margin = 0.5 / _ScaledScreenParams.xy;
                uv = clamp(uv, margin, 1.0 - margin);
                return LightMask(SceneEyeDepth(uv), uv);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = SceneEyeDepth(uv);

                float pixelMask = LightMask(sceneDepth, uv);
                // Preview the exact same occlusion used by the rays; no secondary mesh redraw.
                if (_DebugMask > 0.5)
                {
                    float alpha = 0.55 * (1.0 - pixelMask);
                    return half4(alpha, 0.0, 0.0, alpha);
                }

                // --- fog : épaisseur d'air jusqu'à la surface visible ---
                float fog = 1.0 - exp(-_Density * min(sceneDepth * RadialFactor(uv), _MaxDistance));
                float3 haze = _FogColor.rgb * _Ambient * fog;
                float hazeAlpha = saturate(_Ambient * fog);

                // --- direction du regard et du soleil ---
                float3 camPos = GetCameraPositionWS();
                float3 nearPos = ComputeWorldSpacePosition(uv, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 viewDir = normalize(nearPos - camPos);
                float3 sunDir = normalize(_SunDir.xyz);

                // --- position du soleil à l'écran (même convention d'UV que ComputeWorldSpacePosition) ---
                float4 sunCS = mul(UNITY_MATRIX_VP, float4(sunDir, 0.0));
                if (sunCS.w <= 1e-4 || _Outdoor * fog * _Scattering <= 1e-5)
                    return half4(haze, hazeAlpha);
                float2 sunUV = DirectionUV(sunCS);
                float outside = max(max(-sunUV.x, sunUV.x - 1.0), max(-sunUV.y, sunUV.y - 1.0));
                float edgeFade = 1.0 - smoothstep(-0.02, 0.18, outside);
                if (edgeFade <= 1e-4) return half4(haze, hazeAlpha);

                float cosTheta = dot(viewDir, sunDir);
                if (cosTheta <= 0.0) return half4(haze, hazeAlpha);
                // Fixed world basis around the sun. Head roll changes the projection, not the pattern or its angular width.
                float3 referenceUp = abs(sunDir.y) > 0.99 ? float3(0, 0, 1) : float3(0, 1, 0);
                float3 axisX = normalize(cross(referenceUp, sunDir));
                float3 axisY = cross(sunDir, axisX);
                float2 angular = float2(dot(viewDir, axisX), dot(viewDir, axisY)) / max(cosTheta * 0.6, 1e-4);
                float halo = exp(-dot(angular, angular) * _ShaftFocus);
                if (halo < 1e-4) return half4(haze, hazeAlpha);
                float streak = lerp(1.0, Streaks(atan2(angular.y, angular.x + 1e-6)) * 2.0, _StreakStrength);

                // A finite emitter keeps light visible around the edge of a branch or roof.
                float4 diskX = mul(UNITY_MATRIX_VP, float4(axisX * _SunRadius, 0.0));
                float4 diskY = mul(UNITY_MATRIX_VP, float4(axisY * _SunRadius, 0.0));
                float source = (2.0 * SourceTap(sunCS) + SourceTap(sunCS + diskX) + SourceTap(sunCS - diskX)
                    + SourceTap(sunCS + diskY) + SourceTap(sunCS - diskY)) / 6.0;
                // Keep shafts in visible openings even when the solar disk itself is hidden.
                source = lerp(0.55, 1.0, source);

                int steps = (int)clamp(_Steps, 8.0, 64.0);
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float viewW = max(dot(viewDir, forward), 1e-4);
                float sunW = max(dot(sunDir, forward), 1e-4);
                float lit = 0.0, total = 0.0;
                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float t = (i + 0.5) / steps;
                    // Perspective-correct weights: sample the same world directions when the camera turns.
                    float projectedT = (t * sunW) / lerp(viewW, sunW, t);
                    float2 p = lerp(uv, sunUV, projectedT);
                    float weight = lerp(1.0, 0.45, t);
                    if (all(p > 0.0) && all(p < 1.0)) lit += LightMask(SceneEyeDepth(p), p) * weight;
                    total += weight;
                }
                float visibility = smoothstep(0.18, 0.9, lit / max(total, 1e-4));
                // Scatter in the air before a surface, so silhouettes carve shafts instead of clipping the whole effect.
                float foreground = lerp(0.68, 1.0, pixelMask);
                float phase = min(4.0, HenyeyGreenstein(cosTheta, _Anisotropy) * PI);
                float amount = halo * visibility * foreground * streak * phase * _Scattering * _Outdoor * fog * source * edgeFade;
                float3 light = _SunColor.rgb * _SunIntensity * amount;

                return half4(haze + light, hazeAlpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
