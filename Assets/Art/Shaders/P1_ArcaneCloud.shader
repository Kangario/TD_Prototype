// Мультяшные облачка-туман (URP). Рисует меш ArcaneCloudField: каждое облачко - квад-билборд,
// всё движение в вершинном шейдере (работает и в редакторе без Play, один draw call).
//  - освещение тремя тонами по нормали облачка из атласа (свет тёплый, тень сиреневая), мазок по границе;
//  - край облачка чёткий (порог плотности), при «смерти» облачко съедается с краёв, а не тает прозрачностью;
//  - каждый цикл жизни облачко появляется в новом месте рядом со своей точкой и плывёт по ветру (_P1_Wind);
//  - мягкое пересечение с геометрией (глубина) и исчезание вблизи камеры (зум).
Shader "P1/ArcaneCloud"
{
    Properties
    {
        [NoScaleOffset] _CloudTex ("Puffs atlas 2x2 RG normal B density A noise", 2D) = "black" {}

        [Header(Shading)]
        _LitColor ("Lit", Color) = (1, 0.95, 0.88, 1)
        _MidColor ("Mid", Color) = (0.72, 0.7, 0.86, 1)
        _ShadowColor ("Shadow", Color) = (0.38, 0.37, 0.6, 1)
        _LightInfluence ("Sun color influence", Range(0, 1)) = 0.5
        _LitEdge ("Lit edge N.L", Range(-1, 1)) = 0.4
        _MidEdge ("Shadow edge N.L", Range(-1, 1)) = -0.1
        _ShadeSoftness ("Shade softness", Range(0.001, 0.3)) = 0.035
        _ShadeNoise ("Shade brush noise", Range(0, 1)) = 0.3
        _RimColor ("Silver lining", Color) = (1, 0.88, 0.7, 1)
        _RimStrength ("Silver lining strength", Range(0, 2)) = 0.5

        [Header(Shape)]
        _EdgeCut ("Edge cut", Range(0, 0.9)) = 0.1
        _EdgeSoftness ("Edge softness", Range(0.005, 0.3)) = 0.03
        _EdgeNoise ("Edge noise", Range(0, 0.5)) = 0.14
        _Opacity ("Opacity", Range(0, 1)) = 0.95

        [Header(Fades)]
        _DepthFade ("Soft intersection m", Range(0.01, 10)) = 2
        _NearFadeStart ("Camera fade start m", Float) = 6
        _NearFadeRange ("Camera fade range m", Float) = 10

        [Header(Motion)]
        _CycleSpeed ("Life cycles per second", Float) = 0.022
        _DriftMul ("Drift x global wind", Float) = 1
        _Jitter ("Respawn jitter m", Float) = 5
        _Bob ("Bobbing m", Float) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Clouds"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LitColor;
                half4 _MidColor;
                half4 _ShadowColor;
                half4 _RimColor;
                half _LightInfluence;
                half _LitEdge;
                half _MidEdge;
                half _ShadeSoftness;
                half _ShadeNoise;
                half _RimStrength;
                half _EdgeCut;
                half _EdgeSoftness;
                half _EdgeNoise;
                half _Opacity;
                float _DepthFade;
                float _NearFadeStart;
                float _NearFadeRange;
                float _CycleSpeed;
                float _DriftMul;
                float _Jitter;
                float _Bob;
            CBUFFER_END

            TEXTURE2D(_CloudTex);
            SAMPLER(sampler_CloudTex);

            float4 _P1_Wind;   // xy: ветер по XZ (м/с), ставит ArcaneAtmosphere

            struct Attributes
            {
                float4 positionOS : POSITION;   // точка облачка (у всех 4 вершин одна)
                float4 uv0 : TEXCOORD0;         // xy: угол квада -1..1, z: размер (м), w: вариант 0..3
                float4 uv1 : TEXCOORD1;         // x: фаза 0..1, y: множитель скорости цикла, z: зеркало ±1, w: высота/ширина
                half4 color : COLOR;            // оттенок и непрозрачность облачка
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 tangentWS : TEXCOORD1;   // мир: куда растёт u атласа
                float3 bitangentWS : TEXCOORD2; // мир: куда растёт v
                float3 normalWS : TEXCOORD3;    // к камере
                float3 data : TEXCOORD4;        // x: эрозия 0..1, y: глубина от камеры (м)
                half4 color : COLOR;
            };

            float3 Hash31(float p)
            {
                float3 p3 = frac(p * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xxy + p3.yzz) * p3.zyx);
            }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                float speed = max(_CycleSpeed * v.uv1.y, 1e-4);
                float t = _Time.y * speed + v.uv1.x;
                float cycle = floor(t);
                float life = frac(t);
                float3 rnd = Hash31(cycle * 7.13 + v.uv1.x * 113.7);

                float3 center = TransformObjectToWorld(v.positionOS.xyz);
                center.xz += (rnd.xy - 0.5) * 2.0 * _Jitter;                         // новое место каждый цикл
                center.xz += _P1_Wind.xy * _DriftMul * (life - 0.5) / speed;          // плывёт по ветру
                center.y += sin(_Time.y * 0.35 + v.uv1.x * 6.2832) * _Bob;

                float fade = smoothstep(0.0, 0.2, life) * (1.0 - smoothstep(0.75, 1.0, life));
                float size = v.uv0.z * lerp(0.82, 1.0, fade) * (0.9 + 0.2 * rnd.z);

                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 toCam = UNITY_MATRIX_V[2].xyz;
                float3 posWS = center + right * (v.uv0.x * size * 0.5) + up * (v.uv0.y * size * 0.5 * v.uv1.w);

                o.positionCS = TransformWorldToHClip(posWS);
                float2 local = v.uv0.xy * 0.5 + 0.5;
                local.x = v.uv1.z < 0.0 ? 1.0 - local.x : local.x;
                local = local * 0.98 + 0.01;
                float variant = v.uv0.w;
                o.uv = (local + float2(fmod(variant, 2.0), floor(variant * 0.5))) * 0.5;
                o.tangentWS = right * (v.uv1.z < 0.0 ? -1.0 : 1.0);
                o.bitangentWS = up;
                o.normalWS = toCam;
                o.data = float3(1.0 - fade, -TransformWorldToView(posWS).z, rnd.z);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, i.uv);
                half2 nxy = tex.rg * 2.0h - 1.0h;
                half nz = sqrt(saturate(1.0h - dot(nxy, nxy)));
                float3 n = normalize(i.tangentWS * nxy.x + i.bitangentWS * nxy.y + i.normalWS * nz);

                // --- форма: порог плотности, при угасании порог растёт - облачко съедается с краёв
                // шум края к концу жизни слабеет, иначе облачко рассыпается на точки
                half erosion = (half)i.data.x;
                half dens = tex.b + (tex.a - 0.5h) * _EdgeNoise * (1.0h - 0.8h * erosion);
                half cut = lerp(_EdgeCut, 0.85h, erosion);
                half alpha = smoothstep(cut, cut + _EdgeSoftness, dens);
                alpha *= 1.0h - smoothstep(0.75h, 1.0h, erosion);

                // --- свет: три тона, граница с мазком
                Light light = GetMainLight();
                half ndl = dot(n, light.direction) + (tex.a - 0.5h) * _ShadeNoise;
                half mid = smoothstep(_MidEdge - _ShadeSoftness, _MidEdge + _ShadeSoftness, ndl);
                half lit = smoothstep(_LitEdge - _ShadeSoftness, _LitEdge + _ShadeSoftness, ndl);
                half3 sun = lerp(half3(1, 1, 1), light.color, _LightInfluence);
                half3 col = lerp(_ShadowColor.rgb, _MidColor.rgb, mid);
                col = lerp(col, _LitColor.rgb * sun, lit);
                half rimBand = 1.0h - smoothstep(cut, cut + 0.16h, dens);
                col += rimBand * mid * _RimColor.rgb * _RimStrength;
                col *= i.color.rgb;

                // --- мягкое пересечение с геометрией и исчезание у камеры
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                alpha *= saturate((sceneZ - i.data.y) / _DepthFade);
                alpha *= saturate((i.data.y - _NearFadeStart) / max(_NearFadeRange, 0.01));
                alpha *= _Opacity * i.color.a;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
