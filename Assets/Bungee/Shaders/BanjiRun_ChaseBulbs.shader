// 電光掲示板の縁の電球（BungeeBuilder が 1 つのメッシュにまとめて作る）。
// 頂点色の r・g に電球の番号が入っていて、時間で「何個おきに光る」位置をずらして、光が流れるように見せる。
// _Mode: 0 = 流れる, 1 = 全部光る, 2 = 全部消える（LedSign が文字の点滅に合わせて MaterialPropertyBlock で切り替える）
Shader "BanjiRun/ChaseBulbs"
{
    Properties
    {
        _BaseMap ("Bulb", 2D) = "white" {}
        _OnColor ("On", Color) = (1, 0.85, 0.4, 1)
        _OffColor ("Off", Color) = (0.28, 0.18, 0.08, 0.85)
        _Speed ("Speed (bulbs/sec)", Float) = 10
        _Spacing ("Spacing", Float) = 3
        _Mode ("Mode", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ChaseBulbs"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _OnColor;
                half4 _OffColor;
                float _Speed;
                float _Spacing;
                float _Mode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float index : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.index = round(input.color.r * 255.0) + round(input.color.g * 255.0) * 256.0;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); // VR で両目を一度に描くとき、どちらの目かを決める
                half4 bulb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float head = floor(_Time.y * _Speed);
                float chase = fmod(input.index + head, max(_Spacing, 1.0)) < 0.5 ? 1.0 : 0.0;
                float on = _Mode > 1.5 ? 0.0 : (_Mode > 0.5 ? 1.0 : chase);
                half4 color = lerp(_OffColor, _OnColor, on);
                return half4(color.rgb * bulb.rgb, color.a * bulb.a);
            }
            ENDHLSL
        }
    }
}
