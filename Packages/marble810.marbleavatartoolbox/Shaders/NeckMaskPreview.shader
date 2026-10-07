// NeckMaskMaker 在 Scene 视图中的 Mask 范围叠加预览。
// 只在编辑器中使用（通过 Shader.Find 加载），不会被打包进 Avatar 构建。
Shader "Hidden/MarbleAvatarToolbox/NeckMaskPreview"
{
    Properties
    {
        _MaskTex ("Mask", 2D) = "black" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            // 小幅深度偏移配合视空间前移，避免与原表面共面争夺深度。
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MaskTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                // 沿视线前移 0.1 mm，不改变屏幕位置；不依赖 reversed-Z 的符号。
                float3 viewPosition = UnityObjectToViewPos(input.vertex);
                float scale = max(0.0, (-viewPosition.z - 0.0001) / max(-viewPosition.z, 0.0001));
                viewPosition *= scale;
                output.position = mul(UNITY_MATRIX_P, float4(viewPosition, 1.0));
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float mask = tex2D(_MaskTex, input.uv).r;
                return fixed4(1.0, 40.0 / 255.0, 40.0 / 255.0, mask);
            }
            ENDCG
        }
    }

    Fallback Off
}
