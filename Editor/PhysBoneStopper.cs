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
    /// 全 Renderer)が、空でなく、かつ全部そのトグルの隠蔽対象に含まれること。
    /// 迷う要素があれば止めない側へ倒す:
    /// - 消費者が 1 つでもトグルの外に居れば止めない(素体と共有する胸・スカート等)
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
        /// このトグルで止められる PB の m_Enabled バインディングを plan へ足し、
        /// 止めた PB のパスを返す。
        /// </summary>
        public List<string> AddStopBindings(ToggleCandidate target, HidePlan plan,
            System.Func<EditorCurveBinding, bool> isEnabledAlreadyAnimated)
        {
            var hidden = new HashSet<Renderer>(
                target.Object.GetComponentsInChildren<Renderer>(true));
            var stopped = new List<string>();

            foreach (var pb in _physBones)
            {
                // 対象サブツリー内の PB は disableComponentsWhenHidden の領分
                if (pb.transform.IsChildOf(target.Object.transform)) continue;
                if (!pb.enabled) continue;
                if (_multiPbObjects.Contains(pb.transform)) continue;

                var consumers = ConsumersOf(pb);
                if (consumers.Count == 0) continue;
                if (!consumers.All(hidden.Contains)) continue;

                var path = AnimationUtility.CalculateTransformPath(pb.transform, _root);
                var binding = EditorCurveBinding.FloatCurve(path, typeof(VRCPhysBone), "m_Enabled");
                if (isEnabledAlreadyAnimated(binding)) continue;

                plan.Toggled.Add((binding, 1f, 0f));
                stopped.Add(path);
            }

            return stopped;
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
