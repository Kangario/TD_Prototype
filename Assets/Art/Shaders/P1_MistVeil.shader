// Пелена тумана под тропами (URP 17): одна горизонтальная плоскость вместо облачков.
// Прозрачность зависит от толщины тумана между плоскостью и тем, что за ней (глубина сцены): столбы и острова
// плавно уходят в туман, а там, где за пеленой пусто, она сплошная. Нужен Depth Texture (включён в ассетах URP).
//  - цвет у поверхности и в глубине, крупные мягкие пятна яркости, которые медленно плывут по ветру;
//  - толщина, после которой туман сплошной, задаётся в метрах.
Shader "P1/MistVeil"
{
    Properties
    {
        _Color ("Surface color", Color) = (0.80, 0.85, 0.94, 1)
        _DeepColor ("Deep color (= camera background)", Color) = (0.68, 0.75, 0.89, 1)
        _Thickness ("Opaque after, m", Float) = 16
        _SurfaceAlpha ("Alpha right at the surface", Range(0, 1)) = 0.12
        [NoScaleOffset] _NoiseTex ("Soft noise (A)", 2D) = "gray" {}
        _NoiseScale ("Noise tiles per meter", Float) = 0.008
        _NoiseStrength ("Noise brightness", Range(0, 0.3)) = 0.05
        _Wind ("Wind m/s (xy)", Vector) = (0.6, 0.25, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline"
               "IgnoreProjector" = "True" }

        Pass
        {
            Name "Veil"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _DeepColor;
                float _Thickness;
                half _SurfaceAlpha;
                float _NoiseScale;
                half _NoiseStrength;
                float4 _Wind;
            CBUFFER_END

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
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
                o.screenPos = ComputeScreenPos(p.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float selfEye = i.screenPos.w;                      // перспективная камера: w = глубина по оси взгляда
                float dz = max(sceneEye - selfEye, 0.0);
                // туман однородный: прозрачность по закону Бугера, к _Thickness остаётся ~5%
                half t = 1.0h - exp(-dz * 3.0 / max(_Thickness, 0.01));
                half a = lerp(_SurfaceAlpha, 1.0h, t);
                half n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, (i.positionWS.xz - _Wind.xy * _Time.y) * _NoiseScale).a;
                half3 col = lerp(_Color.rgb, _DeepColor.rgb, t) * (1.0h + (n - 0.5h) * 2.0h * _NoiseStrength);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
