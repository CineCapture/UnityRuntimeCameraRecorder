Shader "Landoria/ExampleLit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        CGPROGRAM
        #pragma surface Surface Lambert fullforwardshadows addshadow

        fixed4 _Color;

        struct Input
        {
            float2 uv_MainTex;
        };

        void Surface(Input input, inout SurfaceOutput output)
        {
            output.Albedo = _Color.rgb;
            output.Alpha = _Color.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
