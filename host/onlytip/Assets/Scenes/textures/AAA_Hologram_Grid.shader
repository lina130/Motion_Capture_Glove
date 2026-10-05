Shader "Custom/AAA_Hologram_Grid_SAFE"
{
    Properties
    {
        // 基础颜色
        _BaseColor ("全息底色", Color) = (0, 0.2, 0.8, 0.3)
        _GlowColor ("主发光色", Color) = (0, 0.7, 1.0, 1.0)
        _EdgeColor ("边缘发光色", Color) = (0.3, 0.9, 1.0, 1.0)
        
        // 网格设置
        _GridDensity ("网格密度", Range(5, 50)) = 18
        _GridThickness ("网格线粗细", Range(0.001, 0.05)) = 0.004
        _GridBrightness ("网格亮度", Range(0, 5)) = 2.2
        
        // 扫描效果
        _ScanSpeed ("扫描速度", Range(0.1, 3)) = 0.8
        _ScanWidth ("扫描线宽度", Range(0.01, 0.2)) = 0.08
        _ScanBrightness ("扫描亮度", Range(0, 10)) = 4.5
        
        // 边缘发光
        _InnerRimPower ("内边缘强度", Range(1, 8)) = 3.5
        _OuterRimPower ("外边缘强度", Range(1, 12)) = 7.0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent+100" 
            "IgnoreProjector"="True"
        }
        LOD 200
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off // 显示模型背面，全息是双面的

        CGPROGRAM
        #pragma surface surf Standard alpha:blend
        #pragma target 3.0

        struct Input
        {
            float3 worldPos;
            float3 viewDir;
        };

        fixed4 _BaseColor;
        fixed4 _GlowColor;
        fixed4 _EdgeColor;
        
        float _GridDensity;
        float _GridThickness;
        float _GridBrightness;
        
        float _ScanSpeed;
        float _ScanWidth;
        float _ScanBrightness;
        
        float _InnerRimPower;
        float _OuterRimPower;
        /*
        // 快速随机函数
        float rand(float2 co)
        {
            return frac(sin(dot(co.xy, float2(12.9898, 78.233))) * 43758.5453);
        }*/

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // 1. 世界空间网格线（绝对不会消失的版本）
            float2 gridUV = IN.worldPos.xy * _GridDensity;
            float2 grid = abs(frac(gridUV - 0.5) - 0.5) / fwidth(gridUV);
            float gridLine = min(grid.x, grid.y);
            gridLine = 1.0 - smoothstep(_GridThickness, _GridThickness * 2, gridLine);

            // 2. 脉冲扫描线
            float scan = frac(IN.worldPos.y * 0.5 - _Time.y * _ScanSpeed);
            scan = smoothstep(1.0 - _ScanWidth, 1.0, scan) * smoothstep(0.0, _ScanWidth * 0.3, scan);
            scan *= 1.0 - abs(scan - 0.5) * 2.0;

            // 3. 双层菲涅尔边缘（全息核心）
            float rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
            float innerRim = pow(rim, _InnerRimPower);
            float outerRim = pow(rim, _OuterRimPower);

            // 4. 轻微闪烁
            float flicker = 0.92 + 0.08 * sin(_Time.y * 1.2 * 6.28);

            // 5. 组合效果（Alpha值故意设高，保证能看到）
            float baseAlpha = _BaseColor.a * flicker;
            float gridAlpha = gridLine * _GridBrightness * flicker * 0.5;
            float edgeAlpha = (innerRim * 0.7 + outerRim * 1.3) * flicker;
            float scanAlpha = scan * _ScanBrightness * flicker * 0.4;

            // 最终输出
            o.Albedo = _BaseColor.rgb * baseAlpha;
            o.Emission = _GlowColor.rgb * (gridLine + scan) + _EdgeColor.rgb * (innerRim + outerRim);
            o.Alpha = baseAlpha + gridAlpha + edgeAlpha + scanAlpha;

            // 关闭不需要的属性
            o.Metallic = 0;
            o.Smoothness = 0;
        }
        ENDCG
    }
    FallBack "Transparent/VertexLit"
}