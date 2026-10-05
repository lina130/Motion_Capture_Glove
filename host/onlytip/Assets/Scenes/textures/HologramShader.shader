Shader "Custom/BlueHologram_Final_Fixed"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0, 0.4, 1, 0.4)
        _GlowColor ("Glow Color", Color) = (0, 0.9, 1, 1)
        _GlowPower ("Glow Intensity", Range(0, 6)) = 2.5
        _RimPower ("Rim Power", Range(0, 10)) = 4
        _NoiseAmount ("Noise Amount", Range(0, 0.3)) = 0.08
        
        // 新增：透明度控制参数
        _MinAlpha ("Center Transparency", Range(0, 1)) = 0.15
        _MaxAlpha ("Edge Transparency", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
        };

        fixed4 _BaseColor;
        fixed4 _GlowColor;
        float _GlowPower;
        float _RimPower;
        float _NoiseAmount;
        float _MinAlpha;
        float _MaxAlpha;

        float random(float2 st)
        {
            return frac(sin(dot(st.xy, float2(12.9898, 78.233))) * 43758.5453);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            half rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
            rim = pow(rim, _RimPower);

            float noise = random(IN.uv_MainTex * _Time.y * 2) * _NoiseAmount;

            o.Albedo = _BaseColor.rgb * (1 - rim * 0.6);
            o.Emission = _GlowColor.rgb * _GlowPower * (rim + noise);
            
            // 修复：Alpha完全由Base Color和参数控制
            o.Alpha = _BaseColor.a * lerp(_MinAlpha, _MaxAlpha, rim);

            o.Metallic = 0;
            o.Smoothness = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}