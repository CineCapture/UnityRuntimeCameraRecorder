Shader "Landoria/ExampleTemporalMotionSmoothing"
{
    Properties
    {
        _MainTex ("Current Frame", 2D) = "white" {}
        _HistoryTex ("Previous Frame", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _HistoryTex;
            sampler2D _CameraMotionVectorsTexture;
            float _HistoryWeight;
            float _MotionBlurStrength;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Interpolators
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Interpolators Vertex(Attributes input)
            {
                Interpolators output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 Fragment(Interpolators input) : SV_Target
            {
                fixed4 unfiltered = tex2D(_MainTex, input.uv);
                float2 velocity = tex2D(_CameraMotionVectorsTexture, input.uv).rg;
                float velocityLength = length(velocity);
                velocity *= min(1.0, 0.018 / max(velocityLength, 0.00001));
                float2 blurStep = velocity * _MotionBlurStrength / 6.0;
                fixed4 current = 0;
                current += tex2D(_MainTex, input.uv - blurStep * 3.0);
                current += tex2D(_MainTex, input.uv - blurStep * 2.0);
                current += tex2D(_MainTex, input.uv - blurStep);
                current += unfiltered;
                current += tex2D(_MainTex, input.uv + blurStep);
                current += tex2D(_MainTex, input.uv + blurStep * 2.0);
                current += tex2D(_MainTex, input.uv + blurStep * 3.0);
                current /= 7.0;

                float2 historyUv = saturate(input.uv - velocity);
                fixed4 history = tex2D(_HistoryTex, historyUv);
                float difference = max(
                    max(abs(current.r - history.r), abs(current.g - history.g)),
                    abs(current.b - history.b));
                float historyAcceptance = saturate(1.0 - difference * 4.0);
                return lerp(current, history, _HistoryWeight * historyAcceptance);
            }
            ENDCG
        }
    }
}
