// バンジーのロープ（LineRenderer 用）。編み目の模様を貼り、目のすぐ近くの部分は透かして消す。
// 板の上を歩く間、ロープは顔の 20〜30cm 前を横切るので、そのまま描くと太い帯が視界の真ん中をふさぐ。外から見るカメラは遠いので普通に見える。
Shader "BanjiRun/Rope"
{
    Properties
    {
        _BaseMap ("Rope", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _FadeNear ("Fade Near (m)", Float) = 0.25
        _FadeFar ("Fade Far (m)", Float) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Rope"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _FadeNear;
                float _FadeFar;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 rope = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor * input.color;
                float distance = length(input.positionWS - GetCameraPositionWS());
                rope.a *= saturate((distance - _FadeNear) / max(_FadeFar - _FadeNear, 0.001));
                return rope;
            }
            ENDHLSL
        }
    }
}
