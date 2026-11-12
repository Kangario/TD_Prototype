// Toon-шейдер проекта P1 (URP). Порт материала M_Toon_Atlas из Blender (Art/_source/toon_lib.py):
//  - 3 ступени света (тень / полутон / свет) по N.L с учётом тени солнца;
//  - ступенчатый блик там, где vertex color R (металл, камни);
//  - тёплый ободок на освещённой стороне;
//  - свечение там, где vertex color G (ступени по углу + горячее ядро), B = ровное свечение.
// Атлас один на всё: T_Atlas. Цвета ступеней - в инспекторе материала.
Shader "P1/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Atlas", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)

        [Header(Light steps)]
        _ShadowTint ("Shadow", Color) = (0.4236, 0.4100, 0.5564, 1)
        _MidTint ("Mid", Color) = (0.7220, 0.7014, 0.7857, 1)
        _LightTint ("Light", Color) = (1.0, 0.9777, 0.9309, 1)
        _MidStep ("Mid step (N.L)", Range(0, 1)) = 0.16
        _LightStep ("Light step (N.L)", Range(0, 1)) = 0.60
        _Ambient ("Ambient (adds to N.L)", Range(0, 0.5)) = 0.03

        [Header(Specular where vertex color R)]
        _ToonSpecColor ("Spec color", Color) = (1.0, 0.9686, 0.8962, 1)
        _SpecThreshold ("Spec size (N.H above)", Range(0.8, 1)) = 0.975
        _SpecStrength ("Spec strength", Range(0, 1)) = 0.40

        [Header(Rim on lit side)]
        _RimColor ("Rim color", Color) = (1.0, 0.8962, 0.7484, 1)
        _RimWidth ("Rim width (N.V below)", Range(0, 0.5)) = 0.106
        _RimStrength ("Rim strength", Range(0, 1)) = 0.22

        [Header(Glow where vertex color G and B)]
        _EmitMult ("Glow brightness", Range(0, 3)) = 1.35
        _GlowHotColor ("Glow hot core", Color) = (0.7674, 0.7354, 0.6262, 1)

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadowTint;
            half4 _MidTint;
            half4 _LightTint;
            half4 _ToonSpecColor;
            half4 _RimColor;
            half4 _GlowHotColor;
            half _MidStep;
            half _LightStep;
            half _Ambient;
            half _SpecThreshold;
            half _SpecStrength;
            half _RimWidth;
            half _RimStrength;
            half _EmitMult;
            half _Cull;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
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
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float3 n = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half lum = dot(light.color, half3(0.2126, 0.7152, 0.0722));
                half ndl = saturate(dot(n, light.direction));
                half lit = ndl * light.shadowAttenuation * lum;
                half x = lit + _Ambient;

                // 1) ступени света
                half3 ramp = x < _MidStep ? _ShadowTint.rgb : (x < _LightStep ? _MidTint.rgb : _LightTint.rgb);
                half3 col = albedo * ramp;

                half4 mask = i.color;
                // 2) блик (Mask.R)
                float3 h = normalize(light.direction + v);
                half spec = step(_SpecThreshold, saturate(dot(n, h))) * step(0.001, lit);
                col += spec * mask.r * _SpecStrength * lerp(_ToonSpecColor.rgb, albedo, 0.5);

                // 3) ободок на освещённой стороне
                half ndv = abs(dot(n, v));
                half rim = step(ndv, _RimWidth) * step(_MidStep, x);
                col += rim * _RimStrength * _RimColor.rgb;

                // 4) свечение (Mask.G), Mask.B - ровное
                half facing = 1.0 - ndv;
                half g = facing < 0.45 ? 1.0 : (facing < 0.80 ? 0.78 : 0.51);
                half3 glow = albedo * _EmitMult * lerp(g, 1.0, mask.b);
                glow += step(facing, 0.22) * (1.0 - mask.b) * _GlowHotColor.rgb;
                col = lerp(col, glow, mask.g);

                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

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
