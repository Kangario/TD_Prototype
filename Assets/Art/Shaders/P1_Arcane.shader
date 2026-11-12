// Arcane-стиль (URP 17): «рисованный» тун для всего - окружения и персонажей.
//  - свет тёплый, тень холодная и насыщенная (а не просто тёмный альбедо);
//  - граница света рвётся мазками кисти (T_ArcaneBrush.R), край падающих теней тоже «нарисован»;
//  - тонкая тёплая кромка на границе света и тени (как подповерхностное рассеяние в Arcane);
//  - штриховка в тени, зерно, крупные тёплые/холодные пятна цвета по миру;
//  - SSAO рисуется как контактная тень цветом тени;
//  - ободок: тёплый на освещённой стороне, холодный «кикер» в тени;
//  - доп. источники (точечные/прожекторы) - ступенями;
//  - тени облаков (глобальные параметры ставит ArcaneAtmosphere);
//  - маски vertex color атласа окружения (R блик, G свечение, B ровное свечение) - галка «Vertex masks»;
//  - обводка для персонажей (проход SRPDefaultUnlit, ширина в пикселях; 0 = выкл).
Shader "P1/Arcane"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)

        [Header(Triangle colors instead of texture)]
        [Toggle(_TRI_COLORS)] _TriColors ("4 colors by triangle (no texture)", Float) = 0
        _TriColor0 ("Color 1", Color) = (0.36, 0.55, 0.25, 1)
        _TriColor1 ("Color 2", Color) = (0.42, 0.62, 0.28, 1)
        _TriColor2 ("Color 3", Color) = (0.30, 0.48, 0.22, 1)
        _TriColor3 ("Color 4", Color) = (0.48, 0.64, 0.30, 1)
        _TriSeed ("Pattern seed", Float) = 0

        [Header(Light and shadow)]
        _LightTint ("Light tint", Color) = (1, 0.97, 0.92, 1)
        _ShadowColor ("Shadow color", Color) = (0.27, 0.26, 0.42, 1)
        _AmbientStrength ("Scene ambient in shadow", Range(0, 1)) = 0.35
        _ShadowSaturation ("Shadow saturation", Range(0.5, 2)) = 1.3
        _ShadowEdge ("Terminator N.L", Range(-1, 1)) = 0.05
        _ShadowSoftness ("Terminator softness", Range(0.001, 0.5)) = 0.05
        _HighlightEdge ("Highlight band N.L", Range(0, 1)) = 0.72
        _HighlightBoost ("Highlight boost", Range(0, 1)) = 0.12
        _TerminatorColor ("Terminator band color", Color) = (1, 0.36, 0.2, 1)
        _TerminatorStrength ("Terminator band", Range(0, 1)) = 0.35

        [Header(Painterly brush)]
        [NoScaleOffset] _BrushTex ("Brush R strokes G grain B hatch A big noise", 2D) = "gray" {}
        _BrushScale ("Brush tiles per meter", Float) = 0.1
        [ToggleUI] _BrushObjectSpace ("Brush in object space for characters", Float) = 0
        _BrushShading ("Strokes on terminator", Range(0, 1)) = 0.35
        _BrushAlbedo ("Strokes on color", Range(0, 0.5)) = 0.1
        _GrainStrength ("Grain", Range(0, 0.5)) = 0.05
        _ShadowWobble ("Painted shadow edge m", Range(0, 1)) = 0.25
        _HatchStrength ("Hatching in shadow", Range(0, 1)) = 0.15
        _HatchColor ("Hatching multiply", Color) = (0.55, 0.5, 0.72, 1)
        _VariationScale ("Color patches per meter", Float) = 0.03
        _VariationStrength ("Color patches", Range(0, 1)) = 0.4
        _VariationWarm ("Patch warm", Color) = (1.08, 0.97, 0.84, 1)
        _VariationCool ("Patch cool", Color) = (0.86, 0.95, 1.08, 1)

        [Header(Specular and rim)]
        _SpecAmount ("Specular without vertex mask", Range(0, 1)) = 0
        _ArcSpecColor ("Spec color", Color) = (1, 0.95, 0.85, 1)
        _SpecSize ("Spec size N.H", Range(0.8, 1)) = 0.965
        _SpecStrength ("Spec strength", Range(0, 2)) = 0.5
        _RimColor ("Rim lit side", Color) = (1, 0.8, 0.58, 1)
        _RimShadowColor ("Kicker rim shadow side", Color) = (0.2, 0.55, 0.8, 1)
        _RimMin ("Rim start 1-N.V", Range(0, 1)) = 0.62
        _RimMax ("Rim end 1-N.V", Range(0, 1)) = 0.82
        _RimStrength ("Rim strength", Range(0, 2)) = 0.3

        [Header(Ambient occlusion)]
        _AOStrength ("SSAO as painted shadow", Range(0, 1)) = 0.75

        [Header(Vertex color masks R spec G glow B flat glow)]
        [ToggleUI] _VertexMasks ("Use vertex color masks", Float) = 0
        _EmitMult ("Glow brightness", Range(0, 6)) = 2
        _GlowHotColor ("Glow hot core", Color) = (1, 0.85, 0.62, 1)
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)

        [Header(Outline pass for characters)]
        _OutlineWidth ("Outline width px 0 off", Range(0, 4)) = 0
        _OutlineColor ("Outline color", Color) = (0.06, 0.035, 0.08, 1)
        _OutlineAlbedo ("Outline takes albedo hue", Range(0, 1)) = 0.35

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _LightTint;
            half4 _ShadowColor;
            half4 _TerminatorColor;
            half4 _HatchColor;
            half4 _VariationWarm;
            half4 _VariationCool;
            half4 _ArcSpecColor;
            half4 _RimColor;
            half4 _RimShadowColor;
            half4 _GlowHotColor;
            half4 _EmissionColor;
            half4 _OutlineColor;
            half4 _TriColor0;
            half4 _TriColor1;
            half4 _TriColor2;
            half4 _TriColor3;
            float _TriSeed;
            half _AmbientStrength;
            half _ShadowSaturation;
            half _ShadowEdge;
            half _ShadowSoftness;
            half _HighlightEdge;
            half _HighlightBoost;
            half _TerminatorStrength;
            float _BrushScale;
            half _BrushObjectSpace;
            half _BrushShading;
            half _BrushAlbedo;
            half _GrainStrength;
            half _ShadowWobble;
            half _HatchStrength;
            float _VariationScale;
            half _VariationStrength;
            half _SpecAmount;
            half _SpecSize;
            half _SpecStrength;
            half _RimMin;
            half _RimMax;
            half _RimStrength;
            half _AOStrength;
            half _VertexMasks;
            half _EmitMult;
            half _OutlineWidth;
            half _OutlineAlbedo;
            half _Cull;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BrushTex);
        SAMPLER(sampler_BrushTex);

        // Глобальные (ставит ArcaneAtmosphere; без него = 0 и теней облаков нет)
        float4 _P1_CloudShadow;    // x: 1/размер тайла (м), y: сила, z: порог, w: мягкость
        float4 _P1_CloudShadow2;   // xy: ветер (м/с), z: высота облаков (м)

        // Мазки в трипланаре: мир для окружения, объект (в метрах) для персонажей.
        half4 SampleBrush(float3 p, float3 n)
        {
            float3 w = abs(n);
            w *= w;
            w *= w;
            w /= (w.x + w.y + w.z + 1e-5);
            half4 x = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, p.zy);
            half4 y = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, p.xz);
            half4 z = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, p.xy);
            return x * w.x + y * w.y + z * w.z;
        }

        void BrushSpace(float3 positionOS, float3 normalOS, float3 positionWS, float3 normalWS,
                        out float3 brushPos, out float3 brushNormal)
        {
            // масштаб объекта, чтобы мазки персонажа были в метрах при любом масштабе импорта
            float s = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
            brushPos = lerp(positionWS, positionOS * s, _BrushObjectSpace) * _BrushScale;
            brushNormal = lerp(normalWS, normalOS, _BrushObjectSpace);
        }

        half3 Saturation(half3 c, half s)
        {
            half l = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
            return max(lerp(l.xxx, c, s), 0.0h);
        }

        // Цвет треугольника: центр треугольника (UV2, пишет ArtImport) в мире -> хеш -> один из 4 цветов.
        // Все три вершины треугольника считают одно и то же, поэтому цвет ровный, без интерполяции.
        // Центр в мире, а не индекс в меше: соседние тайлы не повторяют узор.
        half4 TriangleColor(float3 centerOS)
        {
            float3 c = TransformObjectToWorld(centerOS);
            int3 q = (int3)floor(c * 8.0 + 0.5) + (int)_TriSeed;
            uint h = (uint)q.x * 73856093u ^ (uint)q.y * 19349663u ^ (uint)q.z * 83492791u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            uint k = h & 3u;
            return k == 0u ? _TriColor0 : (k == 1u ? _TriColor1 : (k == 2u ? _TriColor2 : _TriColor3));
        }
        ENDHLSL

        // ------------------------------------------------------------------ основной проход
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _TRI_COLORS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float3 triCenter : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 brushPos : TEXCOORD3;
                float3 brushNormal : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                half4 triColor : TEXCOORD6;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                BrushSpace(v.positionOS.xyz, v.normalOS, o.positionWS, o.normalWS, o.brushPos, o.brushNormal);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                #if defined(_TRI_COLORS)
                    o.triColor = TriangleColor(v.triCenter);
                #endif
                return o;
            }

            // Тени облаков: точка проецируется по лучу солнца на высоту облаков, шум A едет по ветру.
            half CloudShadow(float3 positionWS, float3 L)
            {
                float3 p = positionWS + L * ((_P1_CloudShadow2.z - positionWS.y) / max(L.y, 0.15));
                float2 uv = (p.xz - _P1_CloudShadow2.xy * _Time.y) * _P1_CloudShadow.x;
                half c = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, uv).a;
                return smoothstep(_P1_CloudShadow.z - _P1_CloudShadow.w, _P1_CloudShadow.z + _P1_CloudShadow.w, c)
                       * saturate(_P1_CloudShadow.y);
            }

            // Доп. источник: освещённость ступенями (3 полосы на единицу), кромки ступеней мягкие.
            half3 ToonAdditional(Light l, float3 n, half3 albedo, half strokes)
            {
                half x = saturate(dot(n, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
                x = max(x + strokes * 0.08h * saturate(x * 8.0h), 0.0h);
                half k = x * 3.0h;
                half stepped = (floor(k) + smoothstep(0.82h, 1.0h, frac(k))) / 3.0h;
                return albedo * l.color * stepped;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half4 shadowMask = half4(1, 1, 1, 1);

                // --- кисть
                half4 brush = SampleBrush(i.brushPos, normalize(i.brushNormal));
                half strokes = brush.r - 0.5h;

                // --- цвет: атлас (или цвет треугольника), крупные тёплые/холодные пятна, мазки и зерно
                #if defined(_TRI_COLORS)
                    half3 albedo = i.triColor.rgb * _BaseColor.rgb;
                #else
                    half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                #endif
                half big = SAMPLE_TEXTURE2D(_BrushTex, sampler_BrushTex, i.positionWS.xz * _VariationScale).a;
                albedo *= lerp(1.0h, lerp(_VariationCool.rgb, _VariationWarm.rgb, big), _VariationStrength);
                albedo *= 1.0h + strokes * 2.0h * _BrushAlbedo + (brush.g - 0.5h) * 2.0h * _GrainStrength;
                albedo = max(albedo, 0.0h);
                half3 albedoShadow = Saturation(albedo, _ShadowSaturation);

                // --- главный свет; тень сэмплируется со сдвигом к солнцу по мазку - край тени «нарисован»
                Light mainLight = GetMainLight();
                float3 L = mainLight.direction;
                float3 tL = L - n * dot(L, n);
                tL *= rsqrt(max(dot(tL, tL), 1e-4));
                float3 shadowPos = i.positionWS + tL * (strokes * 2.0 * _ShadowWobble);
                mainLight = GetMainLight(TransformWorldToShadowCoord(shadowPos), shadowPos, shadowMask);

                half ndl = dot(n, L);
                half shade = ndl + strokes * _BrushShading;
                half ndlMask = smoothstep(_ShadowEdge - _ShadowSoftness, _ShadowEdge + _ShadowSoftness, shade);
                half lightMask = ndlMask * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half band = saturate(lightMask * (1.0h - lightMask) * 4.0h);
                half lit = lightMask * (1.0h - CloudShadow(i.positionWS, L));

                half3 sun = mainLight.color * _LightTint.rgb;
                half3 shadowLight = _ShadowColor.rgb + SampleSH(n) * _AmbientStrength;
                half3 cShadow = albedoShadow * shadowLight;
                half3 col = lerp(cShadow, albedo * sun, lit);

                half hi = smoothstep(_HighlightEdge - _ShadowSoftness, _HighlightEdge + _ShadowSoftness, shade) * lit;
                col *= 1.0h + hi * _HighlightBoost;
                col += band * _TerminatorColor.rgb * albedoShadow * _TerminatorStrength;

                // --- штриховка в тени
                half ink = (1.0h - brush.b) * (1.0h - lit) * _HatchStrength;
                col = lerp(col, col * _HatchColor.rgb, ink);

                // --- SSAO как нарисованная контактная тень
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);
                    half occ = smoothstep(0.08h, 0.55h, (1.0h - ao.indirectAmbientOcclusion) + strokes * 0.2h);
                    col = lerp(col, cShadow * 0.8h, occ * _AOStrength);
                #endif

                // --- маски атласа
                half4 mask = _VertexMasks > 0.5h ? i.color : half4(0, 0, 0, 0);

                // --- блик
                float3 h = normalize(L + v);
                half nh = saturate(dot(n, h)) + strokes * 0.02h;
                half spec = smoothstep(_SpecSize - 0.008h, _SpecSize + 0.008h, nh) * lit;
                col += spec * saturate(mask.r + _SpecAmount) * _SpecStrength * sun * lerp(_ArcSpecColor.rgb, albedo, 0.4h);

                // --- ободок: тёплый на свету, холодный кикер в тени
                half ndv = saturate(dot(n, v));
                half rim = smoothstep(_RimMin, _RimMax, (1.0h - ndv) + strokes * 0.1h);
                half3 rimCol = lerp(_RimShadowColor.rgb, _RimColor.rgb * sun, lit);
                col += rim * _RimStrength * rimCol * lerp(1.0h, albedo, 0.5h);

                // --- доп. источники
                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalizedScreenSpaceUV = screenUV;
                    uint lightsCount = GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        Light l = GetAdditionalLight(lightIndex, i.positionWS, shadowMask);
                        col += ToonAdditional(l, n, albedo, strokes);
                    }
                    #endif
                    LIGHT_LOOP_BEGIN(lightsCount)
                        Light l = GetAdditionalLight(lightIndex, i.positionWS, shadowMask);
                        col += ToonAdditional(l, n, albedo, strokes);
                    LIGHT_LOOP_END
                #endif

                // --- свечение (G ступени по углу + горячее ядро, B ровное) и эмиссия
                half facing = 1.0h - ndv;
                half g = facing < 0.45h ? 1.0h : (facing < 0.8h ? 0.78h : 0.51h);
                half3 glow = albedo * _EmitMult * lerp(g, 1.0h, mask.b);
                glow += step(facing, 0.22h) * (1.0h - mask.b) * _GlowHotColor.rgb * _EmitMult * 0.5h;
                col = lerp(col, glow, mask.g);
                col += _EmissionColor.rgb;

                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ обводка (персонажи)
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct AttributesO
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsO
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            VaryingsO vertOutline(AttributesO v)
            {
                VaryingsO o = (VaryingsO)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(posWS);
                float2 dir = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
                float len = length(dir);
                dir = len > 1e-5 ? dir / len : float2(0, 0);
                // толщина в пикселях - не зависит от расстояния до камеры
                cs.xy += dir * (_OutlineWidth * 2.0) / _ScreenParams.xy * cs.w;
                o.positionCS = _OutlineWidth > 0.0 ? cs : float4(0, 0, 0, 0);   // 0 = проход выключен
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(cs.z);
                return o;
            }

            half4 fragOutline(VaryingsO i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                half3 c = lerp(_OutlineColor.rgb, albedo * 0.22h, _OutlineAlbedo);
                return half4(MixFog(c, i.fogFactor), 1.0h);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ тени, глубина, нормали (для SSAO)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct AttributesS
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsS
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsS vertShadow(AttributesS v)
            {
                VaryingsS o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pws = TransformObjectToWorld(v.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 ld = normalize(_LightPosition - pws);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pws, nws, ld));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }

            half4 fragShadow(VaryingsS i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            struct AttributesD
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsD
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsD vertDepth(AttributesD v)
            {
                VaryingsD o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half fragDepth(VaryingsD i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct AttributesN
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsN
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            VaryingsN vertDN(AttributesN v)
            {
                VaryingsN o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 fragDN(VaryingsN i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 n = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(n), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
