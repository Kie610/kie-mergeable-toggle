using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>
    /// 案4: ボーンを複製してスケールを NaN⇔1 で切り替える(NaNimation)。
    ///
    /// - レンダラー全体を同時に隠す用途なので、MA の実装と違い頂点複製は要らない。
    ///   貪欲被覆で選んだボーンの複製とウェイト付替だけで済む。
    /// - 代償として boneCount が「そのレンダラーが跨るボーン数」だけ増える。
    ///   constraintsCount は増えない(初期非表示は scale 0 のシリアライズで表す)。
    /// </summary>
    internal static class NaNimationHider
    {
        public static bool CanApply(SkinnedMeshRenderer renderer)
        {
            // ボーンウェイトを持たない SMR(クロス等)は NaN を届ける先がない
            return renderer.sharedMesh != null && renderer.sharedMesh.bindposeCount > 0
                                               && renderer.bones.Length > 0;
        }

        public static HidePlan Apply(ToggleCandidate target, Transform root, bool initiallyHidden)
        {
            var plan = new HidePlan();
            var scaleBones = new List<Transform>();
            foreach (var renderer in target.Renderers)
                scaleBones.AddRange(AddNaNBones(renderer));

            if (scaleBones.Count == 0) return plan;
            ExcludeFromPhysBones(root, scaleBones);

            foreach (var bone in scaleBones)
            {
                // 初期非表示は scale 0 をそのままシリアライズして表す。
                //
                // MA は同じ状況で VRCScaleConstraint(Weight = NaN)を足しているが、
                // それは MA の NaNimation がプリミティブの一部の頂点しか複製ボーンへ
                // 送らないため。scale 0 では残りの頂点が元の位置に留まり、体表から
                // ボーンまで伸びる巨大な三角形になってしまうので使えない。
                // こちらはレンダラーの全頂点を複製ボーンへ送るので、scale 0 でも
                // 骨格上へ潰れるだけで済み、初期状態の表現として使える。
                //
                // WD ON はシリアライズ値を書き戻す(実測済み)。初期非表示のトグルに
                // とって既定の見た目は「隠れている」なので、0 が正しい既定値になる。
                // 逆に現行の 1 は誤った既定値で、コンストレイントはそれを打ち消すために
                // 必要になっていた。既定値を正せば打ち消す仕組みごと要らなくなる。
                if (initiallyHidden) bone.localScale = Vector3.zero;

                var path = AnimationUtility.CalculateTransformPath(bone, root);
                foreach (var axis in new[] { "x", "y", "z" })
                {
                    plan.Toggled.Add((
                        EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale." + axis),
                        1f,
                        float.NaN));
                }
            }

            return plan;
        }

        /// <summary>
        /// 複製ボーンを PhysBone の対象から外す。
        ///
        /// 複製は元ボーンの子として作るので、元ボーンが PB チェーン内にあると
        /// 複製がチェーンの末端として取り込まれてしまう。実測では Shinano で
        /// physBoneTransformCount が 132 → 284 と倍増し、ランクの計上項目である
        /// PB Transform 数を Poor から VeryPoor へ跨がせていた。
        ///
        /// 統計だけの問題ではなく、末端が1本伸びることで揺れかたそのものが変わる。
        /// </summary>
        private static void ExcludeFromPhysBones(Transform root, List<Transform> addedBones)
        {
            foreach (var pb in root.GetComponentsInChildren<VRCPhysBone>(true))
            {
                var pbRoot = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                if (pbRoot == null) continue;

                pb.ignoreTransforms ??= new List<Transform>();
                foreach (var bone in addedBones)
                {
                    if (!bone.IsChildOf(pbRoot) || bone == pbRoot) continue;
                    if (!pb.ignoreTransforms.Contains(bone)) pb.ignoreTransforms.Add(bone);
                }
            }
        }

        /// <summary>
        /// レンダラーの全頂点を NaN 化できるようにする。
        /// 貪欲セットカバーで「残り頂点を最も多くカバーするボーン」を選び、その複製を
        /// 選択ボーンの子として作成、カバーした頂点の該当ウェイトを複製へ付け替える。
        /// 複製は親と同一姿勢(ローカル恒等・bindpose コピー)なのでスケール 1 の間
        /// スキニングは不変。
        /// </summary>
        private static List<Transform> AddNaNBones(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            var weights = mesh.GetAllBoneWeights().ToArray();
            var bonesPerVertex = mesh.GetBonesPerVertex().ToArray();
            var vertexCount = bonesPerVertex.Length;

            var firstWeightIndex = new int[vertexCount];
            for (int v = 0, w = 0; v < vertexCount; v++)
            {
                firstWeightIndex[v] = w;
                w += bonesPerVertex[v];
            }

            var boneToCount = new Dictionary<int, int>();
            var remaining = new List<int>();
            for (var v = 0; v < vertexCount; v++)
            {
                var hasInfluence = false;
                for (var i = 0; i < bonesPerVertex[v]; i++)
                {
                    var bw = weights[firstWeightIndex[v] + i];
                    if (bw.weight == 0 || bw.boneIndex < 0) continue;
                    hasInfluence = true;
                    boneToCount[bw.boneIndex] = boneToCount.GetValueOrDefault(bw.boneIndex) + 1;
                }

                if (hasInfluence) remaining.Add(v);
            }

            var bones = renderer.bones.ToList();
            var bindposes = mesh.bindposes.ToList();
            var sortedBones = boneToCount.OrderByDescending(kv => kv.Value).Select(kv => kv.Key)
                .Where(b => b < bones.Count && bones[b] != null)
                .ToList();

            var addedBones = new List<Transform>();
            foreach (var boneIndex in sortedBones)
            {
                if (remaining.Count == 0) break;

                var newIndex = bones.Count;
                Transform newBone = null; // 実際にカバーする頂点が見つかってから生成する

                remaining.RemoveAll(v =>
                {
                    for (var i = 0; i < bonesPerVertex[v]; i++)
                    {
                        var bw = weights[firstWeightIndex[v] + i];
                        if (bw.weight == 0 || bw.boneIndex != boneIndex) continue;

                        if (newBone == null)
                        {
                            newBone = new GameObject($"MT_NaN_{renderer.name}_{addedBones.Count}").transform;
                            newBone.SetParent(bones[boneIndex], false);
                            bones.Add(newBone);
                            bindposes.Add(bindposes[boneIndex]);
                            addedBones.Add(newBone);
                        }

                        bw.boneIndex = newIndex;
                        weights[firstWeightIndex[v] + i] = bw;
                        return true;
                    }

                    return false;
                });
            }

            if (addedBones.Count == 0) return addedBones;

            // 非破壊: メッシュを複製してから書き換える
            var newMesh = Object.Instantiate(mesh);
            newMesh.name = mesh.name;
            ObjectRegistry.RegisterReplacedObject(mesh, newMesh);
            newMesh.bindposes = bindposes.ToArray();
            using (var nativeBpv = new NativeArray<byte>(bonesPerVertex, Allocator.Temp))
            using (var nativeWeights = new NativeArray<BoneWeight1>(weights, Allocator.Temp))
            {
                newMesh.SetBoneWeights(nativeBpv, nativeWeights);
            }

            renderer.sharedMesh = newMesh;
            renderer.bones = bones.ToArray();
            return addedBones;
        }

    }
}
