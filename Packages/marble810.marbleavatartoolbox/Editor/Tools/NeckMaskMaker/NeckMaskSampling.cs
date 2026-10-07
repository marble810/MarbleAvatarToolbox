using UnityEngine;

namespace marble810.MarbleAvatarToolbox.NeckMaskMaker
{
    /// <summary>
    /// Mask 的距离衰减方式。数值会写入 Compute Shader 的 <c>_SamplingMode</c>，不要调整既有顺序。
    /// </summary>
    internal enum NeckMaskSamplingMode
    {
        /// <summary>线性衰减 <c>1 - t</c>，无附加参数。</summary>
        Linear = 0,

        /// <summary>平滑衰减：标准 smoothstep 的广义形式，由曲率控制弯曲程度。</summary>
        Smooth = 1,

        /// <summary>二值衰减：按阈值直接输出 0 或 1。</summary>
        Constant = 2,
    }

    /// <summary>
    /// 衰减模式的参数契约。界面滑杆范围、默认值与 GPU 计算都以此为准，
    /// 保证窗口显示的值与实际参与运算的参数完全一致。
    /// </summary>
    internal static class NeckMaskSampling
    {
        /// <summary>曲率下限：曲线很快脱离边界线，Mask 只会贴着循环线。</summary>
        internal const float MinCurvature = 0.25f;

        /// <summary>曲率上限：曲线贴近边界才下跌，Mask 更饱满。</summary>
        internal const float MaxCurvature = 4f;

        /// <summary>曲率默认值 1，即标准 smoothstep。</summary>
        internal const float DefaultCurvature = 1f;

        /// <summary>二值阈值默认值。</summary>
        internal const float DefaultThreshold = 0.5f;

        /// <summary>下拉框选项名与枚举顺序一致；界面与 Shader 都只认这三个英文名。</summary>
        internal static readonly string[] ModeNames = { "Linear", "Smooth", "Constant" };

        /// <summary>按模式收敛参数值：平滑取曲率上下限，二值取 0–1，线性没有参数。</summary>
        internal static float ClampParameter(NeckMaskSamplingMode mode, float value)
        {
            switch (mode)
            {
                case NeckMaskSamplingMode.Smooth: return Mathf.Clamp(value, MinCurvature, MaxCurvature);
                case NeckMaskSamplingMode.Constant: return Mathf.Clamp01(value);
                default: return 0f;
            }
        }
    }
}
