using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>
    /// トグルで隠した衣装のためだけに存在するアーマチュア側の PhysBone を、
    /// 隠蔽と同じカーブで止める。
    ///
    /// 判定(保守側): PB のチェーン(rootTransform の子孫 − ignoreTransforms 部分木。
    /// root 自身は動かないので含めない)の消費者(ウェイト &gt; 0 の SMR + チェーン配下の
    /// 全 Renderer)が空でなく、かつ全部いずれかの所有トグルの隠蔽対象に含まれること。
    /// 迷う要素があれば止めない側へ倒す:
    /// - 消費者が 1 つでも全所有トグルの外に居れば止めない(素体と共有する胸等)
    /// - 消費者が空の PB は止めない(何のためにあるか分からないものへ触らない)
    /// - 編集時に無効な PB は触らない(作者が静的に切った状態を尊重する)
    /// - m_Enabled が既にアニメーションされている PB は触らない(他ギミックとの競合回避)
    /// - 同じ GameObject に複数の PhysBone がある場合は触らない(m_Enabled の
    ///   アニメーションバインディングは (パス, 型) で 1 本しか作れず、個別に狙えない)
    ///
    /// VRCPhysBoneCollider は止めない(参照元の PB が止まっていればシミュレーション
    /// コストは発生せず、共有判定を別途持つ価値がない)。Contact も対象外。
    /// </summary>
    internal sealed class PhysBoneStopper
    {
        internal sealed class SharedGroup
        {
            public readonly List<ToggleCandidate> Owners;
            public readonly List<(string path, EditorCurveBinding binding)> PhysBones = new();

            public SharedGroup(IEnumerable<ToggleCandidate> owners)
            {
                Owners = owners.ToList();
            }
        }

        internal sealed class Classification
        {
            public readonly Dictionary<ToggleCandidate, List<(string path, EditorCurveBinding binding)>> Exclusive
                = new();
            public readonly List<SharedGroup> Shared = new();
        }

        private readonly Transform _root;
        private readonly List<VRCPhysBone> _physBones;
        private readonly HashSet<Transform> _multiPbObjects;
        private readonly Dictionary<Transform, HashSet<Renderer>> _consumersByBone;

        private PhysBoneStopper(Transform root, List<VRCPhysBone> physBones,
            HashSet<Transform> multiPbObjects, Dictionary<Transform, HashSet<Renderer>> consumersByBone)
        {
            _root = root;
            _physBones = physBones;
            _multiPbObjects = multiPbObjects;
            _consumersByBone = consumersByBone;
        }

        /// <summary>アバター全体の「ボーン → 消費レンダラー」対応を 1 回だけ作る。</summary>
        public static PhysBoneStopper Build(Transform root)
        {
            var physBones = root.GetComponentsInChildren<VRCPhysBone>(true).ToList();
            var multiPb = new HashSet<Transform>(physBones
                .GroupBy(pb => pb.transform)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key));

            var consumers = new Dictionary<Transform, HashSet<Renderer>>();

            void Add(Transform bone, Renderer renderer)
            {
                if (!consumers.TryGetValue(bone, out var set))
                    consumers[bone] = set = new HashSet<Renderer>();
                set.Add(renderer);
            }

            // ウェイト > 0 の SMR。bones 配列は未使用ボーンも含むことがあるので、
            // メッシュの実ウェイトから使用インデックスを起こす
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var bones = smr.bones;
                var used = new HashSet<int>();
                var weights = mesh.GetAllBoneWeights();
                for (var i = 0; i < weights.Length; i++)
                    if (weights[i].weight > 0f) used.Add(weights[i].boneIndex);
                foreach (var idx in used)
                    if (idx >= 0 && idx < bones.Length && bones[idx] != null)
                        Add(bones[idx], smr);
            }

            // チェーンのボーンに直接ぶら下がる Renderer(静的な飾り等)
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                for (var t = renderer.transform; t != null && t != root; t = t.parent)
                    Add(t, renderer);

            return new PhysBoneStopper(root, physBones, multiPb, consumers);
        }

        /// <summary>
        /// 全トグルを同時に見て、単独所有と共有所有へ分類する。
        /// AnimationIndex の競合判定は、この結果からカーブを追加する前に全件済ませる。
        /// </summary>
        public Classification Classify(IReadOnlyList<ToggleCandidate> targets,
            System.Func<EditorCurveBinding, bool> isEnabledAlreadyAnimated)
        {
            var hiddenByTarget = targets.ToDictionary(
                target => target,
                target => new HashSet<Renderer>(
                    target.Object.GetComponentsInChildren<Renderer>(true)));
            var groups = new Dictionary<string, SharedGroup>();
            var result = new Classification();
            foreach (var target in targets)
                result.Exclusive[target] = new List<(string, EditorCurveBinding)>();

            foreach (var pb in _physBones)
            {
                if (!pb.enabled) continue;
                if (_multiPbObjects.Contains(pb.transform)) continue;

                var consumers = ConsumersOf(pb);
                if (consumers.Count == 0) continue;

                var owners = targets
                    .Where(target => consumers.Any(hiddenByTarget[target].Contains))
                    .ToList();
                if (owners.Count == 0) continue;

                var covered = new HashSet<Renderer>();
                foreach (var owner in owners) covered.UnionWith(hiddenByTarget[owner]);
                if (!consumers.All(covered.Contains)) continue;

                // 対象サブツリー内の PB は disableComponentsWhenHidden の領分。
                // 単独所有では旧 AddStopBindings と同じ条件になる。
                if (owners.Any(owner => pb.transform.IsChildOf(owner.Object.transform))) continue;

                var path = AnimationUtility.CalculateTransformPath(pb.transform, _root);
                var binding = EditorCurveBinding.FloatCurve(path, typeof(VRCPhysBone), "m_Enabled");
                if (isEnabledAlreadyAnimated(binding)) continue;

                if (owners.Count == 1)
                {
                    result.Exclusive[owners[0]].Add((path, binding));
                }
                else
                {
                    var key = string.Join("\n", owners.Select(owner => owner.Path).OrderBy(x => x));
                    if (!groups.TryGetValue(key, out var group))
                        groups[key] = group = new SharedGroup(owners);
                    group.PhysBones.Add((path, binding));
                }
            }

            result.Shared.AddRange(groups.OrderBy(group => group.Key).Select(group => group.Value));
            return result;
        }

        private HashSet<Renderer> ConsumersOf(VRCPhysBone pb)
        {
            var result = new HashSet<Renderer>();
            var chainRoot = pb.rootTransform != null ? pb.rootTransform : pb.transform;
            var ignores = new HashSet<Transform>(
                (pb.ignoreTransforms ?? new List<Transform>()).Where(t => t != null));

            var stack = new Stack<Transform>();
            foreach (Transform child in chainRoot) stack.Push(child);
            while (stack.Count > 0)
            {
                var bone = stack.Pop();
                if (ignores.Contains(bone)) continue;
                if (_consumersByBone.TryGetValue(bone, out var set)) result.UnionWith(set);
                foreach (Transform child in bone) stack.Push(child);
            }

            return result;
        }
    }
}
