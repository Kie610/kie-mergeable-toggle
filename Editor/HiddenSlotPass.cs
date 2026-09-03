using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>
    /// 第 1 パス (変換) から第 2 パス (スロット差し替え) へ渡す情報。
    /// 隠蔽シェイプ名 → 所有トグル。AAO は統合時にシェイプ名へ接頭辞を付けるが
    /// `MT_Hide_` 以降は変えないので、そこから後ろで引く。
    /// </summary>
    internal sealed class HiddenSlotState
    {
        internal sealed class ToggleInfo
        {
            public string Path;
            public bool InitiallyHidden;
            /// <summary>AAP `MT_Hidden/&lt;パス&gt;`。FX に作れなかったトグルは null</summary>
            public string Parameter;
        }

        public bool Enabled;
        public readonly Dictionary<string, ToggleInfo> ByShape = new();
    }

    /// <summary>
    /// 隠れている間だけマテリアルスロットを空シェーダへ差し替え、draw call と頂点処理を消す。
    ///
    /// infinimation は頂点を遠方へ飛ばすだけなので、隠れたメッシュのスロットも描画は
    /// 発行され続ける (lilToon で 1 スロット約 3 draw call)。統合後のメッシュは隠れた衣装の
    /// スロットを抱えたままになり、隠しているのに AAO 単独より重くなる (2026-09-02 実測)。
    ///
    /// AAO の統合の後に走らせる。統合メッシュの各サブメッシュについて、頂点を覆う隠蔽
    /// シェイプの集合を所有トグルとし、所有トグルが全部非表示のときだけ空マテリアルへ
    /// 差し替える。素体と共有するスロット (覆われない頂点を含む) は触らない。
    /// - 所有トグルが 1 つ: そのトグルのクリップへ PPtr カーブを足す
    /// - 複数: AAP の AND ゲートレイヤー `MT_SlotOff n` を作る (共有 PB 停止と同じ形)
    /// SMR 数もスロット数も変わらないので、ランクの計上項目は動かない。
    /// </summary>
    internal static class HiddenSlotPass
    {
        public const string ShaderName = "Hidden/kieMergeableToggle/Empty";
        public const string MaterialName = "MT_Empty";
        public const string LayerPrefix = "MT_SlotOff";
        private const string ShapeMarker = "MT_Hide_";

        private sealed class GateGroup
        {
            public List<string> Parameters;
            public bool DefaultStopped;
            public readonly List<(EditorCurveBinding binding, Material original)> Slots = new();
        }

        public static void Run(BuildContext context)
        {
            var state = context.GetState<HiddenSlotState>();
            if (!state.Enabled || state.ByShape.Count == 0) return;

            // Android はシェーダの許可リストがあり、同梱シェーダが通らない。差し替えても
            // フォールバックで描かれるだけなので作らない。
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                Debug.Log("[MergeableToggle] hidden slot swap is skipped on Android (shader allowlist)");
                return;
            }

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[MergeableToggle] shader '{ShaderName}' not found; hidden slot swap skipped");
                return;
            }

            var root = context.AvatarRootObject;
            var asc = context.Extension<AnimatorServicesContext>();
            Material empty = null;
            var groups = new Dictionary<string, GateGroup>();
            var log = new List<string>();
            var single = 0;
            var shared = 0;

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;

                var infoByShape = new Dictionary<int, HiddenSlotState.ToggleInfo>();
                var ownerOf = new int[mesh.vertexCount];
                for (var v = 0; v < ownerOf.Length; v++) ownerOf[v] = -1;
                Vector3[] deltas = null;
                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    var name = mesh.GetBlendShapeName(i);
                    var at = name.IndexOf(ShapeMarker, StringComparison.Ordinal);
                    if (at < 0) continue;
                    // AAO は統合時に接頭辞に加えて末尾へ連番を付けることがある
                    // (例: `AAO_Merged_11_Cloth_boots__MT_Hide_Cloth_boots_Cloth_boots_5`)。
                    // `MT_Hide_` 以降が控えた名前で始まるものを、最長一致で引く。
                    var tail = name.Substring(at);
                    var info = state.ByShape
                        .Where(pair => tail.StartsWith(pair.Key, StringComparison.Ordinal))
                        .OrderByDescending(pair => pair.Key.Length)
                        .Select(pair => pair.Value)
                        .FirstOrDefault();
                    if (info == null)
                    {
                        Debug.LogWarning($"[MergeableToggle] hide shape '{name}' has no owner record; ignored");
                        continue;
                    }

                    infoByShape[i] = info;
                    deltas ??= new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(i, mesh.GetBlendShapeFrameCount(i) - 1, deltas, null, null);
                    for (var v = 0; v < deltas.Length; v++)
                        if (deltas[v] != Vector3.zero) ownerOf[v] = i;
                }
                if (infoByShape.Count == 0) continue;

                var path = AnimationUtility.CalculateTransformPath(smr.transform, root.transform);
                var materials = smr.sharedMaterials;
                var changed = false;
                for (var sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                {
                    var owners = new HashSet<int>();
                    var uncovered = false;
                    foreach (var v in mesh.GetIndices(sub))
                    {
                        if (ownerOf[v] < 0)
                        {
                            uncovered = true;
                            break;
                        }
                        owners.Add(ownerOf[v]);
                    }

                    var slotLabel = $"{path}[{sub}] {(materials[sub] != null ? materials[sub].name : "null")}";
                    if (uncovered || owners.Count == 0)
                    {
                        log.Add($"  {slotLabel}: kept (contains always-visible vertices)");
                        continue;
                    }
                    if (materials[sub] == null)
                    {
                        log.Add($"  {slotLabel}: kept (no material)");
                        continue;
                    }

                    var binding = EditorCurveBinding.PPtrCurve(
                        path, typeof(SkinnedMeshRenderer), $"m_Materials.Array.data[{sub}]");
                    if (asc.AnimationIndex.GetClipsForBinding(binding).Any())
                    {
                        log.Add($"  {slotLabel}: kept (material is already animated)");
                        continue;
                    }

                    var ownerList = owners.OrderBy(i => infoByShape[i].Path, StringComparer.Ordinal).ToList();
                    var original = materials[sub];
                    if (ownerList.Count == 1)
                    {
                        var shapeBinding = EditorCurveBinding.FloatCurve(
                            path, typeof(SkinnedMeshRenderer), "blendShape." + mesh.GetBlendShapeName(ownerList[0]));
                        if (!asc.AnimationIndex.GetClipsForBinding(shapeBinding).Any())
                        {
                            log.Add($"  {slotLabel}: kept (owner shape is not animated)");
                            continue;
                        }

                        empty ??= CreateEmptyMaterial(context, shader);
                        var emptyMaterial = empty;
                        asc.AnimationIndex.EditClipsByBinding(new[] { shapeBinding }, clip =>
                        {
                            var curve = clip.GetFloatCurve(shapeBinding);
                            if (curve == null) return;
                            clip.SetObjectCurve(binding, curve.keys.Select(key => new ObjectReferenceKeyframe
                            {
                                time = key.time,
                                value = key.value >= 50f ? emptyMaterial : original,
                            }).ToArray());
                        });
                        single++;
                        log.Add($"  {slotLabel}: swapped by toggle {infoByShape[ownerList[0]].Path}");
                    }
                    else
                    {
                        var missing = ownerList.Where(i => infoByShape[i].Parameter == null).ToList();
                        if (missing.Count > 0)
                        {
                            log.Add($"  {slotLabel}: kept (owner without MT_Hidden parameter: " +
                                    string.Join(", ", missing.Select(i => infoByShape[i].Path)) + ")");
                            continue;
                        }

                        empty ??= CreateEmptyMaterial(context, shader);
                        var key = string.Join("\n", ownerList.Select(i => infoByShape[i].Path));
                        if (!groups.TryGetValue(key, out var group))
                        {
                            groups[key] = group = new GateGroup
                            {
                                Parameters = ownerList.Select(i => infoByShape[i].Parameter).ToList(),
                                DefaultStopped = ownerList.All(i => smr.GetBlendShapeWeight(i) > 50f),
                            };
                        }
                        group.Slots.Add((binding, original));
                        shared++;
                        log.Add($"  {slotLabel}: swapped by gate of {ownerList.Count} toggles (" +
                                string.Join(", ", ownerList.Select(i => infoByShape[i].Path)) + ")");
                    }

                    // 初期状態: 所有トグルが全部非表示なら、シリアライズ値も空マテリアルにする
                    // (片方向トグルは WD でこの値へ戻る)
                    if (ownerList.All(i => smr.GetBlendShapeWeight(i) > 50f))
                    {
                        materials[sub] = empty;
                        changed = true;
                    }
                }
                if (changed) smr.sharedMaterials = materials;
            }

            if (groups.Count > 0)
            {
                var fx = asc.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX];
                var index = 0;
                foreach (var group in groups.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value))
                {
                    var layerName = $"{LayerPrefix} {++index}";
                    var emptyMaterial = empty;
                    AndGateLayer.Add(fx, layerName, group.Parameters, group.DefaultStopped,
                        active =>
                        {
                            foreach (var (binding, original) in group.Slots)
                                active.SetObjectCurve(binding, SingleKey(original));
                        },
                        stopped =>
                        {
                            foreach (var (binding, _) in group.Slots)
                                stopped.SetObjectCurve(binding, SingleKey(emptyMaterial));
                        });
                    log.Add($"  {layerName}: {group.Slots.Count} slots, owners " +
                            string.Join(", ", group.Parameters));
                }
            }

            Debug.Log($"[MergeableToggle] hidden slot swap: {single} slots by owner toggle, " +
                      $"{shared} slots by {groups.Count} gate layers\n" + string.Join("\n", log));
        }

        private static Material CreateEmptyMaterial(BuildContext context, Shader shader)
        {
            var material = new Material(shader) { name = MaterialName };
            context.AssetSaver.SaveAsset(material);
            return material;
        }

        private static ObjectReferenceKeyframe[] SingleKey(Material material)
        {
            return new[] { new ObjectReferenceKeyframe { time = 0f, value = material } };
        }
    }
}
