Shader "Landoria/ExampleWindGrass"
{
    Properties
    {
        _MainTex ("Shape", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.2, 0.46, 0.1, 1)
        _TipColor ("Tip Color", Color) = (0.62, 0.86, 0.28, 1)
        _WindStrength ("Wind Strength", Range(0, 1.5)) = 0.9
        _WindSpeed ("Wind Speed", Range(0, 4)) = 1.45
    }

    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        AlphaToMask On

        CGPROGRAM
        #pragma surface Surface Lambert vertex:Vertex fullforwardshadows addshadow

        fixed4 _BaseColor;
        fixed4 _TipColor;
        sampler2D _MainTex;
        float _WindStrength;
        float _WindSpeed;

        struct Input
        {
            float2 uv_MainTex;
            fixed4 color : COLOR;
        };

        void Vertex(inout appdata_full vertex)
        {
            float3 worldPosition = mul(unity_ObjectToWorld, vertex.vertex).xyz;
            float primary = sin(worldPosition.x * 0.72 + worldPosition.z * 0.41 + _Time.y * _WindSpeed);
            float detail = sin(worldPosition.x * 1.83 - worldPosition.z * 1.37 + _Time.y * _WindSpeed * 1.7);
            float gustWave = 0.5 + 0.5 * sin(_Time.y * _WindSpeed * 0.38 + worldPosition.z * 0.12);
            float gust = lerp(0.16, 1.0, gustWave * gustWave);
            float tipWeight = vertex.texcoord.y * vertex.texcoord.y;
            float bend = tipWeight * _WindStrength * gust;
            vertex.vertex.x += (0.88 + primary * 0.18 + detail * 0.065) * bend;
            vertex.vertex.z += (0.34 + primary * 0.12 - detail * 0.075) * bend;
            vertex.vertex.y -= bend * tipWeight * 0.48;
        }

        void Surface(Input input, inout SurfaceOutput output)
        {
            float centered = abs(input.uv_MainTex.x * 2.0 - 1.0);
            float halfWidth = 1.0 - input.uv_MainTex.y * 0.82;
            clip(halfWidth - centered);
            fixed3 gradient = lerp(_BaseColor.rgb, _TipColor.rgb, input.uv_MainTex.y);
            float variation = lerp(0.78, 1.12, input.color.g);
            output.Albedo = gradient * variation;
            output.Alpha = 1.0;
        }
        ENDCG
    }

    FallBack "Transparent/Cutout/VertexLit"
}
