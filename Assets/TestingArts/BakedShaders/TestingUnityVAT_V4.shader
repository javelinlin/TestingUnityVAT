// jave.lin : 2024/11/30
// unity vat 的 shader 测试

Shader "Test/TestingUnityVAT_V4"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _VATTex ("VAT Texture", 2D) = "black" {}
        _PackData0 ("R : Duration, G : FPS, B : PlayTimeOffset, A : IsLoop", Vector) = (2, 30, 0, 1)
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _VATTex;
            float4 _VATTex_TexelSize;
            float4 _PackData0;

            #define _Duration           _PackData0.r
            #define _FPS                _PackData0.g
            #define _PlayTimeOffset     _PackData0.b
            #define _IsLoop             _PackData0.a

            inline float CalcVatAnimationTime(float time)
            {
                return (time % _Duration) * _FPS;
            }

            inline float4 CalcVatTexCoord(uint vertexId, float animationTime)
            {
                return float4(animationTime, vertexId + 0.5, 0, 0) * _VATTex_TexelSize;
            }

            Varyings vert(Attributes v, uint vertexID : SV_VertexID)
            {
                Varyings o;
                // TODO jave.lin : _Time.y 换成另一个，比如，每一举战斗的时候，重置这个 _BattleTime 给到战斗用
                // 这样每一举战斗的 shader 中的 时间驱动就不受整体程序运行时长影响了
                float time = CalcVatAnimationTime(_Time.y + _PlayTimeOffset);
                float4 uv = CalcVatTexCoord(vertexID, time);
                v.positionOS.xyz = tex2Dlod(_VATTex, uv).rgb;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 col = tex2D(_MainTex, i.uv);
                return col;
            }
            ENDHLSL
        }
    }
}