using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace marble810.MarbleAvatarToolbox.NeckMaskMaker
{
    public partial class NeckMaskMaker
    {
        /// <summary>目标对象在材质槽列表中的角色名，同时用作分栏标题与文件名前缀。</summary>
        private const string BodyBaseRole = "Body_base";
        private const string BodyRole = "Body";

        [Serializable]
        private sealed class MaterialSlotSelection
        {
            public GameObject target;
            public Renderer renderer;
            public Mesh sourceMesh;
            public int slotIndex;
            public Material material;
            public bool enabled = true;
            public string role;

            // 不使用哈希值作为身份，避免不同对象或槽的缓存碰撞。
            public long Key => ((long)renderer.GetInstanceID() << 32) | (uint)slotIndex;
            public int SubMeshIndex => Math.Min(slotIndex, sourceMesh.subMeshCount - 1);
            public string Label => $"{role} [{slotIndex}] { (material != null ? material.name : NeckMaskLoc.T("空材质")) }";
            public bool IsValid => target != null && renderer != null && sourceMesh != null
                && material != null && slotIndex >= 0 && sourceMesh.subMeshCount > 0
                && sourceMesh.GetTopology(SubMeshIndex) == MeshTopology.Triangles
                && sourceMesh.GetIndexCount(SubMeshIndex) > 0;
        }

        /// <summary>源网格和槽身份不变时保留用户勾选；不按 Material 合并槽。</summary>
        private void SynchronizeMaterialSlots()
        {
            if (_materialSlots == null) _materialSlots = new List<MaterialSlotSelection>();
            var next = new List<MaterialSlotSelection>();
            AppendMaterialSlots(_bodyBase, BodyBaseRole, next);
            if (_body != _bodyBase) AppendMaterialSlots(_body, BodyRole, next);
            bool changed = next.Count != _materialSlots.Count;
            if (!changed)
                for (int i = 0; i < next.Count; i++)
                    if (!ReferenceEquals(next[i], _materialSlots[i])) { changed = true; break; }
            if (!changed) return;
            _materialSlots = next;
            ResetMaterialSlotResources();
        }

        private void AppendMaterialSlots(GameObject target, string role, List<MaterialSlotSelection> next)
        {
            var renderer = GetRenderer(target);
            var mesh = GetRendererMesh(target);
            if (renderer == null || mesh == null || mesh.subMeshCount == 0) return;
            var materials = renderer.sharedMaterials;
            int count = Math.Max(materials.Length, mesh.subMeshCount);
            for (int i = 0; i < count; i++)
            {
                var material = i < materials.Length ? materials[i] : null;
                var entry = _materialSlots.Find(s => s != null && s.target == target && s.renderer == renderer
                    && s.sourceMesh == mesh && s.slotIndex == i && s.material == material);
                if (entry == null)
                    entry = new MaterialSlotSelection
                    {
                        target = target, renderer = renderer, sourceMesh = mesh,
                        slotIndex = i, material = material, role = role,
                    };
                entry.role = role;
                next.Add(entry);
            }
        }

        private List<MaterialSlotSelection> GetSelectedMaterialSlots()
        {
            SynchronizeMaterialSlots();
            var selected = new List<MaterialSlotSelection>();
            var keys = new HashSet<long>();
            foreach (var slot in _materialSlots)
                if (slot.IsValid && slot.enabled && keys.Add(slot.Key)) selected.Add(slot);
            return selected;
        }

        private void DrawMaterialSlotSection()
        {
            SynchronizeMaterialSlots();

            BeginCard(NeckMaskLoc.T("目标材质槽"));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(NeckMaskLoc.T("全选"))) SetAllMaterialSlots(true);
                if (GUILayout.Button(NeckMaskLoc.T("全不选"))) SetAllMaterialSlots(false);
            }

            EditorGUILayout.Space(4f);

            // Body_base 与 Body 各自一栏，栏内只显示本对象的槽。
            var bodyBaseSlots = new List<MaterialSlotSelection>();
            var bodySlots = new List<MaterialSlotSelection>();
            foreach (var slot in _materialSlots)
            {
                if (slot.role == BodyBaseRole) bodyBaseSlots.Add(slot);
                else bodySlots.Add(slot);
            }

            if (bodyBaseSlots.Count == 0 && bodySlots.Count == 0)
            {
                EditorGUILayout.HelpBox(NeckMaskLoc.T("没有可用的材质槽，请先设置 Body 与 Body_base。"), MessageType.Info);
            }
            else if (bodyBaseSlots.Count == 0 || bodySlots.Count == 0)
            {
                // 只有一个对象时不分栏，整行显示。
                bool hasBodyBase = bodyBaseSlots.Count > 0;
                DrawMaterialSlotColumn(hasBodyBase ? BodyBaseRole : BodyRole,
                    hasBodyBase ? bodyBaseSlots : bodySlots, 0f);
            }
            else
            {
                float columnWidth = ResolveMaterialSlotColumnWidth();
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawMaterialSlotColumn(BodyBaseRole, bodyBaseSlots, columnWidth);
                    GUILayout.Space(MaterialColumnSpacing);
                    DrawMaterialSlotColumn(BodyRole, bodySlots, columnWidth);
                }
            }

            if (GetSelectedMaterialSlots().Count == 0)
            {
                EditorGUILayout.HelpBox(NeckMaskLoc.T("请至少勾选一个有效材质槽。"), MessageType.Warning);
            }

            EndCard();
        }

        /// <summary>等分两栏时每栏的宽度；扣除 Card 内边距与滚动条占位。</summary>
        private static float ResolveMaterialSlotColumnWidth()
        {
            float available = Mathf.Max(160f,
                EditorGUIUtility.currentViewWidth - (CardHorizontalPadding + CardBorderPixels) * 2f
                - CardOuterMargin * 2f - ScrollViewScrollbarAllowance);
            return (available - MaterialColumnSpacing) * 0.5f;
        }

        private void DrawMaterialSlotColumn(string title, List<MaterialSlotSelection> slots, float width)
        {
            var options = width > 0f ? GUILayout.Width(width) : GUILayout.ExpandWidth(true);
            using (new EditorGUILayout.VerticalScope(options))
            {
                EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
                if (slots.Count == 0)
                {
                    EditorGUILayout.LabelField(NeckMaskLoc.T("无可用槽"), EditorStyles.miniLabel);
                    return;
                }

                foreach (var slot in slots) DrawMaterialSlotRow(slot);
            }
        }

        private void DrawMaterialSlotRow(MaterialSlotSelection slot)
        {
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!slot.IsValid))
            {
                bool enabled = EditorGUILayout.Toggle(slot.enabled, GUILayout.Width(18f));
                if (enabled != slot.enabled)
                {
                    Undo.RecordObject(this, NeckMaskLoc.T("选择目标材质槽"));
                    slot.enabled = enabled;
                    ResetMaterialSlotResources();
                }

                EditorGUILayout.LabelField($"[{slot.slotIndex}]", EditorStyles.miniLabel, GUILayout.Width(30f));
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(slot.material, typeof(Material), false);
            }

            if (!slot.IsValid)
                EditorGUILayout.LabelField(NeckMaskLoc.T("此槽无有效材质或三角形，不能烘焙。"), EditorStyles.miniLabel);
        }

        private void SetAllMaterialSlots(bool enabled)
        {
            Undo.RecordObject(this, NeckMaskLoc.T("选择目标材质槽"));
            foreach (var slot in _materialSlots)
                if (slot.IsValid) slot.enabled = enabled;
            ResetMaterialSlotResources();
        }

        private void ResetMaterialSlotResources()
        {
            // 面板借用烘焙器 RT，必须先清除记录再释放 GPU 资源。
            ReleaseTexturePreviews();
            _gpuBaker?.Dispose();
            _gpuBaker = null;
            _gpuTargetErrors.Clear();
            _gpuPreviewVersions.Clear();
            foreach (var mesh in _previewMeshes.Values)
                if (mesh != null) DestroyImmediate(mesh);
            _previewMeshes.Clear();
            _textureDirty = true;
            if (_preview || _showTexturePreview) SceneView.RepaintAll();
        }

        private Texture2D BakeMaterialSlotTexture(MaterialSlotSelection slot, out int coveredPixels)
        {
            coveredPixels = 0;
            if (_gpuBaker == null || _gpuTargetErrors.ContainsKey(slot.Key))
                throw new InvalidOperationException(NeckMaskLoc.F("{0}：没有可导出的遮罩。", slot.Label));
            var baked = _gpuBaker.Find(slot.Key);
            if (baked == null) throw new InvalidOperationException(NeckMaskLoc.F("{0}：未烘焙。", slot.Label));
            coveredPixels = baked.CoveredPixels;
            return NeckMaskGpuBaker.Readback(baked.Texture);
        }

        private string MaterialSlotFileName(string prefix, MaterialSlotSelection slot)
        {
            return $"{prefix}_{slot.role}_slot{slot.slotIndex}_{SanitizeFileName(slot.material.name)}.png";
        }

        private void WriteMaterialSlotMasks(List<MaterialSlotSelection> slots, List<string> paths, ref int saved)
        {
            if (slots.Count != paths.Count || slots.Count == 0)
                throw new InvalidOperationException(NeckMaskLoc.T("目标槽与输出路径不匹配。"));
            var pngs = EncodeMaterialSlotMasks(slots);
            for (int i = 0; i < paths.Count; i++)
            {
                File.WriteAllBytes(paths[i], pngs[i]);
                saved++;
                ImportAsMaskAsset(paths[i]);
            }
        }

        /// <summary>先准备全部 PNG，任一槽失败时不开始写文件。</summary>
        private List<byte[]> EncodeMaterialSlotMasks(List<MaterialSlotSelection> slots)
        {
            if (!TryEnsureGpuTextures(out string error)) throw new InvalidOperationException(error);
            foreach (var slot in slots)
                if (_gpuTargetErrors.TryGetValue(slot.Key, out string slotError))
                    throw new InvalidOperationException(slotError);
            var pngs = new List<byte[]>(slots.Count);
            foreach (var slot in slots)
            {
                var texture = BakeMaterialSlotTexture(slot, out _);
                try { pngs.Add(texture.EncodeToPNG()); }
                finally { DestroyImmediate(texture); }
            }
            return pngs;
        }
    }
}
