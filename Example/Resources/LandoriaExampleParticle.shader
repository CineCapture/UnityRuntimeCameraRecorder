Shader "Landoria/ExampleParticle"
{
    Properties
    {
        _TintColor ("Tint Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _TintColor;

            struct VertexInput
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            struct VertexOutput
            {
                float4 position : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            VertexOutput Vertex(VertexInput input)
            {
                VertexOutput output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _TintColor;
                output.uv = input.uv;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 Fragment(VertexOutput input) : SV_Target
            {
                float2 centered = input.uv * 2.0 - 1.0;
                float radialAlpha = saturate(1.0 - dot(centered, centered));
                radialAlpha *= radialAlpha;
                fixed4 color = fixed4(input.color.rgb, input.color.a * radialAlpha);
                UNITY_APPLY_FOG(input.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
}
