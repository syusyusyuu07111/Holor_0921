Shader "Universal Render Pipeline/Custom/RandomNoise_Animated"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Scale   ("UV Scale", Float) = 1.0
        _Speed   ("Scroll Speed", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "Queue"="Geometry"
            "RenderPipeline"="UniversalPipeline"
            "UniversalMaterialType"="Unlit"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float  _Scale;
            float  _Speed;

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            // 疑似ランダム（連続だけど高周波）
            float hash21(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            half4 frag (Varyings i) : SV_Target
            {
                // 粒度と時間スクロール
                float2 uv = i.uv * _Scale;
                float t = _Time.y * _Speed;

                // 斜め方向に流す
                float2 uvAnim = uv + float2(t, t * 0.73);

                float c = hash21(uvAnim);
                return half4(c, c, c, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
