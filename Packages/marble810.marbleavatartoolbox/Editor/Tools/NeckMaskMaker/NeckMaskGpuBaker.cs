using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace marble810.MarbleAvatarToolbox.NeckMaskMaker
{
    /// <summary>
    /// 路线 B 的常驻 GPU 烘焙器。CPU 只建立 UV 分桶与贡献区间；重心采样、距离、
    /// 衰减、重叠平均、合并、外扩都在 GPU。映射初始化允许一次覆盖计数读回，拖动不读回。
    /// </summary>
    internal sealed class NeckMaskGpuBaker : IDisposable
    {
        private const string ShaderPath = "Packages/marble810.marbleavatartoolbox/Shaders/NeckMaskBake.compute";
        private const int TileSize = 16;
        private const int MaxSamples = 8 * 1024 * 1024;
        private const int MaxTileEntries = 8 * 1024 * 1024;

        [StructLayout(LayoutKind.Sequential)]
        private struct Range { public int start, count; }
        [StructLayout(LayoutKind.Sequential)]
        private struct FaceBounds { public int minX, minY, maxX, maxY; }

        internal sealed class Target : IDisposable
        {
            internal Vector2[] uvs;
            internal int[] indices;
            internal ComputeBuffer ranges, samples, fields, vertices, triangles;
            internal RenderTexture raw;
            internal int geometryVersion = -1, mappedGeometryVersion = -1;
            internal int sampleCount, rawVersion, packedRawVersion = -1;
            internal float maxDistance = float.NaN, samplingParameter;
            internal int samplingMode = -1, dilation = -1;
            internal byte[] coverage;
            public RenderTexture Texture { get; internal set; }
            public int CoveredPixels { get; internal set; }

            public void Dispose()
            {
                ranges?.Release(); samples?.Release(); fields?.Release(); vertices?.Release();
                triangles?.Release();
                ReleaseTexture(raw); ReleaseTexture(Texture);
            }
        }

        private readonly ComputeShader _shader;
        private readonly Dictionary<long, Target> _targets = new Dictionary<long, Target>();
        private readonly int _count, _buildSamples, _buildFields, _mapSurface, _merge, _dilate, _pack;
        private ComputeBuffer _segments;
        private int _loopVersion = -1;
        private RenderTexture _ping, _pong, _mergedRaw, _mergedTexture;
        private int _size;

        public static bool IsSupported(out string error)
        {
            error = null;
            if (!SystemInfo.supportsComputeShaders
                || !SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, FormatUsage.LoadStore)
                || !SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, FormatUsage.LoadStore))
            {
                error = NeckMaskLoc.T("当前设备不支持 GPU Mask 所需的 Compute / RT 格式，Mask 预览与导出不可用。");
                return false;
            }
            if (AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath) == null)
            {
                error = NeckMaskLoc.T("找不到 NeckMaskBake.compute，请刷新 Package。");
                return false;
            }
            return true;
        }

        public NeckMaskGpuBaker(int size)
        {
            if (!IsSupported(out string error)) throw new InvalidOperationException(error);
            _size = size;
            _shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath));
            _shader.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                _count = _shader.FindKernel("CountCoverage");
                _buildSamples = _shader.FindKernel("BuildSamples");
                _buildFields = _shader.FindKernel("BuildFields");
                _mapSurface = _shader.FindKernel("MapSurface");
                _merge = _shader.FindKernel("Merge");
                _dilate = _shader.FindKernel("Dilate");
                _pack = _shader.FindKernel("Pack");
            }
            catch { UnityEngine.Object.DestroyImmediate(_shader); throw; }
        }

        public int Size => _size;
        public Target Find(long id) => _targets.TryGetValue(id, out var target) ? target : null;

        public Target Prepare(long id, Vector2[] uvs, int[] indices, Vector3[] worldVertices,
            Vector3[] segments, int geometryVersion,
            float maxDistance, NeckMaskSamplingMode samplingMode, float samplingParameter, int dilation)
        {
            if (!_targets.TryGetValue(id, out var target) || target.uvs != uvs || target.indices != indices)
            {
                target?.Dispose();
                _targets.Remove(id);
                target = CreateTarget(uvs, indices, worldVertices.Length);
                _targets.Add(id, target);
            }

            if (target.geometryVersion != geometryVersion)
            {
                SetLoop(segments, geometryVersion);
                target.vertices.SetData(worldVertices);
                _shader.SetBuffer(_buildFields, "_Vertices", target.vertices);
                _shader.SetBuffer(_buildFields, "_Triangles", target.triangles);
                _shader.SetBuffer(_buildFields, "_Samples", target.samples);
                _shader.SetBuffer(_buildFields, "_Distances", target.fields);
                _shader.SetBuffer(_buildFields, "_Segments", _segments);
                _shader.SetInt("_SampleCount", target.sampleCount);
                int groups = (target.sampleCount + 63) / 64;
                int groupsX = Math.Min(groups, 65535);
                _shader.SetInt("_FieldRowWidth", groupsX * 64);
                _shader.Dispatch(_buildFields, groupsX, (groups + groupsX - 1) / groupsX, 1);
                target.geometryVersion = geometryVersion;
            }

            // 收敛后的参数同时决定缓存键与写入 Shader 的值，避免界面显示与计算结果不一致。
            float parameter = NeckMaskSampling.ClampParameter(samplingMode, samplingParameter);
            bool remap = target.mappedGeometryVersion != geometryVersion
                || target.maxDistance != maxDistance
                || target.samplingMode != (int)samplingMode || target.samplingParameter != parameter;
            if (remap)
            {
                int kernel = _mapSurface;
                _shader.SetBuffer(kernel, "_PixelRanges", target.ranges);
                _shader.SetBuffer(kernel, "_Distances", target.fields);
                _shader.SetFloat("_MaxDistance", Mathf.Max(0.0001f, maxDistance));
                _shader.SetInt("_SamplingMode", (int)samplingMode);
                _shader.SetFloat("_SamplingParameter", parameter);
                _shader.SetTexture(kernel, "_RawOutput", target.raw);
                DispatchPixels(kernel);
                target.maxDistance = maxDistance;
                target.samplingMode = (int)samplingMode;
                target.samplingParameter = parameter;
                target.mappedGeometryVersion = geometryVersion;
                target.rawVersion++;
            }
            if (target.packedRawVersion != target.rawVersion || target.dilation != dilation)
            {
                PackDilated(target.raw, target.Texture, dilation);
                target.packedRawVersion = target.rawVersion;
                target.dilation = dilation;
            }
            return target;
        }

        /// <summary>先选择高优先级原始覆盖，再外扩；不能先外扩各目标再合并。</summary>
        public RenderTexture Compose(Target low, Target high, int dilation, out int coveredPixels)
        {
            if (low == null && high == null) throw new InvalidOperationException(NeckMaskLoc.T("没有可烘焙的目标网格。"));
            if (low == null || high == null)
            {
                var only = high ?? low;
                coveredPixels = only.CoveredPixels;
                return only.Texture;
            }
            EnsureTexture(ref _mergedRaw, GraphicsFormat.R32G32_SFloat, "NeckMask merged raw");
            EnsureTexture(ref _mergedTexture, GraphicsFormat.R8G8B8A8_UNorm, "NeckMask merged final");
            _shader.SetTexture(_merge, "_RawLow", low.raw);
            _shader.SetTexture(_merge, "_RawHigh", high.raw);
            _shader.SetTexture(_merge, "_RawOutput", _mergedRaw);
            DispatchPixels(_merge);
            PackDilated(_mergedRaw, _mergedTexture, dilation);
            coveredPixels = 0;
            for (int i = 0; i < low.coverage.Length; i++)
                if (low.coverage[i] != 0 || high.coverage[i] != 0) coveredPixels++;
            return _mergedTexture;
        }

        public static Texture2D Readback(RenderTexture texture)
        {
            var previous = RenderTexture.active;
            Texture2D result = null;
            try
            {
                result = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                RenderTexture.active = texture;
                result.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                result.Apply(false, false);
                return result;
            }
            catch { if (result != null) UnityEngine.Object.DestroyImmediate(result); throw; }
            finally { RenderTexture.active = previous; }
        }

        private Target CreateTarget(Vector2[] uvs, int[] indices, int vertexCount)
        {
            if (uvs == null || uvs.Length != vertexCount) throw new InvalidOperationException(NeckMaskLoc.T("目标网格没有有效 UV0。"));
            var target = new Target { uvs = uvs, indices = indices };
            ComputeBuffer uvBuffer = null, boundsBuffer = null, tilesBuffer = null, facesBuffer = null;
            try
            {
                BuildTiles(uvs, indices, out var bounds, out var tiles, out var tileFaces);
                target.triangles = Buffer(indices, 4);
                target.vertices = new ComputeBuffer(vertexCount, 12);
                target.ranges = new ComputeBuffer(_size * _size, 8);
                uvBuffer = Buffer(uvs, 8);
                boundsBuffer = Buffer(bounds, 16);
                tilesBuffer = Buffer(tiles, 8);
                facesBuffer = Buffer(tileFaces, 4);
                foreach (int kernel in new[] { _count, _buildSamples })
                {
                    _shader.SetBuffer(kernel, "_UVs", uvBuffer);
                    _shader.SetBuffer(kernel, "_Triangles", target.triangles);
                    _shader.SetBuffer(kernel, "_FaceBounds", boundsBuffer);
                    _shader.SetBuffer(kernel, "_TileRanges", tilesBuffer);
                    _shader.SetBuffer(kernel, "_TileFaces", facesBuffer);
                    _shader.SetBuffer(kernel, "_PixelRanges", target.ranges);
                }
                _shader.SetInt("_TileSize", TileSize);
                _shader.SetInt("_TilesX", (_size + TileSize - 1) / TileSize);
                DispatchPixels(_count);
                // 仅映射初始化读回一次；保留所有重叠贡献，不截断、不平均距离。
                var pixels = new Range[_size * _size];
                target.ranges.GetData(pixels);
                target.coverage = new byte[pixels.Length];
                long sampleCount = 0;
                for (int p = 0; p < pixels.Length; p++)
                {
                    pixels[p].start = (int)sampleCount;
                    sampleCount += pixels[p].count;
                    if (sampleCount > MaxSamples)
                        throw new InvalidOperationException(NeckMaskLoc.T("UV 重叠贡献超过 GPU 缓存安全上限，请降低 Texture Size。"));
                    if (pixels[p].count > 0) { target.coverage[p] = 1; target.CoveredPixels++; }
                }
                if (sampleCount == 0) throw new InvalidOperationException(NeckMaskLoc.T("UV 烘焙覆盖为空，请确认 UV0 位于 0–1 范围内。"));
                target.sampleCount = (int)sampleCount;
                target.ranges.SetData(pixels);
                target.samples = new ComputeBuffer(target.sampleCount, 12);
                target.fields = new ComputeBuffer(target.sampleCount, 4);
                _shader.SetBuffer(_buildSamples, "_Samples", target.samples);
                DispatchPixels(_buildSamples);
                EnsureTexture(ref target.raw, GraphicsFormat.R32G32_SFloat, "NeckMask target raw");
                RenderTexture final = null;
                EnsureTexture(ref final, GraphicsFormat.R8G8B8A8_UNorm, "NeckMask target final");
                target.Texture = final;
                return target;
            }
            catch { target.Dispose(); throw; }
            finally { uvBuffer?.Release(); boundsBuffer?.Release(); tilesBuffer?.Release(); facesBuffer?.Release(); }
        }

        private void BuildTiles(Vector2[] uvs, int[] indices, out FaceBounds[] bounds, out Range[] ranges, out int[] faces)
        {
            int tilesX = (_size + TileSize - 1) / TileSize;
            var lists = new List<int>[tilesX * tilesX];
            bounds = new FaceBounds[indices.Length / 3];
            int entries = 0;
            for (int face = 0; face < bounds.Length; face++)
            {
                bounds[face] = new FaceBounds { minX = 1, minY = 1, maxX = 0, maxY = 0 };
                int a = indices[face * 3], b = indices[face * 3 + 1], c = indices[face * 3 + 2];
                if (a < 0 || b < 0 || c < 0 || a >= uvs.Length || b >= uvs.Length || c >= uvs.Length) continue;
                var p = uvs[a]; var q = uvs[b]; var r = uvs[c];
                float minX = Mathf.Min(p.x, Mathf.Min(q.x, r.x)), maxX = Mathf.Max(p.x, Mathf.Max(q.x, r.x));
                float minY = Mathf.Min(p.y, Mathf.Min(q.y, r.y)), maxY = Mathf.Max(p.y, Mathf.Max(q.y, r.y));
                float area = (q.x - p.x) * (r.y - p.y) - (r.x - p.x) * (q.y - p.y);
                if (float.IsNaN(area) || float.IsInfinity(area) || Mathf.Abs(area) < 1e-12f
                    || maxX < 0 || minX > 1 || maxY < 0 || minY > 1) continue;
                var bound = new FaceBounds
                {
                    minX = Mathf.Clamp(Mathf.FloorToInt(minX * _size) - 1, 0, _size - 1),
                    minY = Mathf.Clamp(Mathf.FloorToInt(minY * _size) - 1, 0, _size - 1),
                    maxX = Mathf.Clamp(Mathf.CeilToInt(maxX * _size) + 1, 0, _size - 1),
                    maxY = Mathf.Clamp(Mathf.CeilToInt(maxY * _size) + 1, 0, _size - 1),
                };
                bounds[face] = bound;
                for (int y = bound.minY / TileSize; y <= bound.maxY / TileSize; y++)
                for (int x = bound.minX / TileSize; x <= bound.maxX / TileSize; x++)
                {
                    if (++entries > MaxTileEntries)
                        throw new InvalidOperationException(NeckMaskLoc.T("UV 三角形分桶超过安全上限，请降低 Texture Size 或检查重叠 UV。"));
                    int tile = y * tilesX + x;
                    if (lists[tile] == null) lists[tile] = new List<int>();
                    lists[tile].Add(face);
                }
            }
            ranges = new Range[lists.Length];
            faces = new int[entries];
            int offset = 0;
            for (int tile = 0; tile < lists.Length; tile++)
            {
                int count = lists[tile]?.Count ?? 0;
                ranges[tile] = new Range { start = offset, count = count };
                if (count > 0) lists[tile].CopyTo(faces, offset);
                offset += count;
            }
        }

        private void SetLoop(Vector3[] segments, int version)
        {
            if (segments == null || segments.Length < 2) throw new InvalidOperationException(NeckMaskLoc.T("没有有效颈部线段。"));
            if (_loopVersion == version) return;
            if (_segments == null || _segments.count != segments.Length)
            {
                _segments?.Release();
                _segments = new ComputeBuffer(segments.Length, 12);
            }
            _segments.SetData(segments);
            _shader.SetInt("_SegmentCount", segments.Length / 2);
            _loopVersion = version;
        }

        private void PackDilated(RenderTexture raw, RenderTexture destination, int dilation)
        {
            RenderTexture source = raw;
            if (dilation > 0)
            {
                EnsureTexture(ref _ping, GraphicsFormat.R32G32_SFloat, "NeckMask dilation ping");
                EnsureTexture(ref _pong, GraphicsFormat.R32G32_SFloat, "NeckMask dilation pong");
                for (int i = 0; i < dilation; i++)
                {
                    var next = i % 2 == 0 ? _ping : _pong;
                    _shader.SetTexture(_dilate, "_RawInput", source);
                    _shader.SetTexture(_dilate, "_RawOutput", next);
                    DispatchPixels(_dilate);
                    source = next;
                }
            }
            _shader.SetTexture(_pack, "_RawInput", source);
            _shader.SetTexture(_pack, "_PackedOutput", destination);
            DispatchPixels(_pack);
        }

        private void DispatchPixels(int kernel)
        {
            _shader.SetInt("_Size", _size);
            _shader.Dispatch(kernel, (_size + 7) / 8, (_size + 7) / 8, 1);
        }

        private void EnsureTexture(ref RenderTexture texture, GraphicsFormat format, string name)
        {
            if (texture != null) return;
            texture = new RenderTexture(new RenderTextureDescriptor(_size, _size)
            {
                graphicsFormat = format, depthBufferBits = 0, msaaSamples = 1,
                enableRandomWrite = true, useMipMap = false, autoGenerateMips = false,
            }) { name = name, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!texture.Create())
            {
                ReleaseTexture(texture); texture = null;
                throw new InvalidOperationException(NeckMaskLoc.T("无法创建 GPU Mask RenderTexture，请降低 Texture Size。"));
            }
        }

        private static ComputeBuffer Buffer<T>(T[] data, int stride) where T : struct
        {
            var buffer = new ComputeBuffer(Math.Max(1, data.Length), stride);
            try
            {
                if (data.Length > 0) buffer.SetData(data);
                else buffer.SetData(new T[1]);
                return buffer;
            }
            catch { buffer.Release(); throw; }
        }

        private static void ReleaseTexture(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }

        public void Dispose()
        {
            foreach (var target in _targets.Values) target.Dispose();
            _targets.Clear();
            _segments?.Release(); _segments = null;
            ReleaseTexture(_ping); ReleaseTexture(_pong); ReleaseTexture(_mergedRaw); ReleaseTexture(_mergedTexture);
            _ping = _pong = _mergedRaw = _mergedTexture = null;
            if (_shader != null) UnityEngine.Object.DestroyImmediate(_shader);
        }
    }
}
