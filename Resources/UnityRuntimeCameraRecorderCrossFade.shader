Shader "UnityRuntimeCameraRecorder/CrossFade"
{
    Properties
    {
        _MainTex ("Outgoing", 2D) = "black" {}
        _IncomingTex ("Incoming", 2D) = "black" {}
        _Blend ("Blend", Range(0, 1)) = 0
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _IncomingTex;
            float _Blend;

            fixed4 Fragment(v2f_img input) : SV_Target
            {
                fixed4 outgoing = tex2D(_MainTex, input.uv);
                fixed4 incoming = tex2D(_IncomingTex, input.uv);
                return lerp(outgoing, incoming, _Blend);
            }
            ENDCG
        }
    }
}
