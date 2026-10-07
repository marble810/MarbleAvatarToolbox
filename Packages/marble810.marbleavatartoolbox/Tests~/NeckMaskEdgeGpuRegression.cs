// UnityMCP execute_code / Roslyn 方法体：真实 GPU 验证水平、斜线、竖线的中心与宽度。
// 只创建临时 HideAndDontSave 对象；finally 移除相机、网格、材质、RT。
var tool = AppDomain.CurrentDomain.GetAssemblies()
    .Select(a => a.GetType("marble810.MarbleAvatarToolbox.NeckMaskMaker.NeckMaskMaker"))
    .First(t => t != null);
var build = tool.GetMethod("BuildScreenSpaceLineMesh", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
if (build == null) throw new Exception("缺少屏幕空间线绘制路径");
var shader = Shader.Find("Hidden/MarbleAvatarToolbox/NeckMaskEdge");
if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("边线 Shader 编译失败");
var go = new GameObject("NeckMaskEdgeGpuTest") { hideFlags = HideFlags.HideAndDontSave };
var camera = go.AddComponent<Camera>();
camera.enabled = false;
camera.cullingMask = 0;
camera.clearFlags = CameraClearFlags.SolidColor;
camera.backgroundColor = Color.black;
camera.nearClipPlane = 0.01f;
camera.farClipPlane = 100f;
var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
var meshes = new System.Collections.Generic.List<Mesh>();
var previous = RenderTexture.active;
var output = new System.Collections.Generic.List<string>();
try
{
    rt.Create();
    camera.targetTexture = rt;
    material.SetColor("_Color", Color.white);
    material.SetFloat("_HalfWidth", 1.5f);
    material.SetVector("_ViewportSize", new Vector4(256, 256, 0, 0));
    foreach (bool ortho in new[] { false, true })
    foreach (float depth in new[] { 1f, 3f })
    foreach (int angle in new[] { 0, 45, 90 })
    {
        camera.orthographic = ortho;
        camera.orthographicSize = 1;
        camera.fieldOfView = depth == 1f ? 40f : 70f;
        Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        Vector2 a = new Vector2(128, 128) - direction * 70;
        Vector2 b = new Vector2(128, 128) + direction * 70;
        var points = new[] { camera.ScreenToWorldPoint(new Vector3(a.x, a.y, depth)),
            camera.ScreenToWorldPoint(new Vector3(b.x, b.y, depth)) };
        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        meshes.Add(mesh);
        if (!(bool)build.Invoke(null, new object[] { mesh, camera, points, 2.5f })) throw new Exception("构建失败");
        var commands = new UnityEngine.Rendering.CommandBuffer();
        commands.DrawMesh(mesh, Matrix4x4.identity, material);
        camera.AddCommandBuffer(UnityEngine.Rendering.CameraEvent.AfterEverything, commands);
        Texture2D pixels = null;
        try
        {
            camera.Render();
            RenderTexture.active = rt;
            pixels = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
            pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            pixels.Apply();
            Vector2 normal = new Vector2(-direction.y, direction.x);
            float sum = 0, weighted = 0, thickness = 0;
            // 在中心周围按 1/4 像素采样，使用亮度积分而不是易抖动的二值像素计数。
            for (int i = -32; i <= 32; i++)
            {
                float d = i * 0.25f;
                Vector2 p = new Vector2(128, 128) + normal * d;
                float value = pixels.GetPixelBilinear(p.x / 256, p.y / 256).r;
                sum += value;
                weighted += d * value;
                thickness += value * 0.25f;
            }
            float center = sum > 0 ? weighted / sum : 100;
            if (Mathf.Abs(center) > 0.85f || thickness < 2.3f || thickness > 3.8f)
                throw new Exception("线中心/宽度错误：ortho=" + ortho + " depth=" + depth + " angle=" + angle + " center=" + center + " width=" + thickness);
            output.Add("ortho=" + ortho + ", depth=" + depth + ", angle=" + angle + ", center=" + center.ToString("F3") + ", width=" + thickness.ToString("F3"));
        }
        finally
        {
            camera.RemoveCommandBuffer(UnityEngine.Rendering.CameraEvent.AfterEverything, commands);
            commands.Release();
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
finally
{
    RenderTexture.active = previous;
    camera.targetTexture = null;
    foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
    UnityEngine.Object.DestroyImmediate(material);
    rt.Release();
    UnityEngine.Object.DestroyImmediate(rt);
    UnityEngine.Object.DestroyImmediate(go);
}
return "PASS: 12 个 GPU 视角/深度组合\n" + string.Join("\n", output);
