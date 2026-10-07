// 颈部边循环：真实顶点中心线 + 裁剪空间像素扩展，避免 Handles AA 线的投影偏差。
Shader "Hidden/MarbleAvatarToolbox/NeckMaskEdge"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.75, 0.1, 1)
        _HalfWidth ("Half Width", Float) = 1.5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+101" "RenderType" = "Transparent" }
        Pass
        {
            ZWrite Off
            // 选边是 X-Ray 辅助线；逐像素深度裁切会让轮廓线半边消失并随视角忽粗忽细。
            // 红色 Mask 表面预览仍使用正常深度测试，与选边线互不影响。
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _Color;
            float _HalfWidth;
            float4 _ViewportSize;
            struct appdata
            {
                float4 vertex : POSITION;
                float2 pixelOffset : TEXCOORD0;
                float2 distance : TEXCOORD1;
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                noperspective float distance : TEXCOORD0;
            };
            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                // Scene 的渲染纹理可能翻转投影 Y；像素法线也必须同步翻转。
                // 否则斜线的横向扩展会变成沿线扩展，在约 45° 处几乎消失。
                output.position.xy += input.pixelOffset * float2(1.0, _ProjectionParams.x)
                    * (2.0 / _ViewportSize.xy) * output.position.w;
                output.distance = input.distance.x;
                return output;
            }
            float4 frag(v2f input) : SV_Target
            {
                // 固定一像素的解析抗锯齿，不依赖世界尺寸、距离或 Handles 贴图采样。
                float coverage = 1.0 - smoothstep(_HalfWidth - 0.5, _HalfWidth + 0.5, abs(input.distance));
                return float4(_Color.rgb, _Color.a * coverage);
            }
            ENDCG
        }
    }
    Fallback Off
}
