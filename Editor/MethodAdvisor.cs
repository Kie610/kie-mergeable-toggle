using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>
    /// トグルごとに使う機構を自動で決める。
    ///
    /// 判定は「畳んだあとに実際どれだけ面が見えるか」で行う。太さや辺長のような
    /// 間接的な指標では、静止時は消えているのにポーズで出てくる破綻を捕まえられない
    /// (この件では指標を3回間違えている。Docs/hiding-mechanisms.md 参照)。
    ///
    /// 静止時点で面が残るもの (スカート・ドレス・コート) は、寄せ先をどう選んでも
    /// 畳めない。複数のボーンチェーンに跨り、そのボーンが体外にあるため。
    /// そこだけ UV タイル破棄へ回し、残りはシェイプ方式の中で良い方を選ぶ。
    /// </summary>
    internal static class MethodAdvisor
    {
        /// <summary>
        /// 静止時の残存面積比がこれを超えたら「畳めない」と判断して UV タイル破棄へ回す。
        ///
        /// 実測値(残存/元): Shinano タイツ 1.7%、Velno スラックス 0%、
        /// Hanka コート 22%、Selena セーラー襟 30%、Shinano スカート 54%。
        /// 畳める群と畳めない群の間が広く空いているので、その中間に置く。
        /// </summary>
        private const float StaticResidualRatio = 0.10f;

        /// <summary>
        /// 静止時の残存が絶対面積でこれ未満なら、比率が大きくても UV タイル破棄へ回さない
        /// (m²)。比率だけで見ると小さいメッシュを拾いすぎる。
        ///
        /// UV タイル破棄へ回したレンダラーはシェイプ方式とは別の統合グループへ分かれるので、
        /// 1件増やすごとに SMR とマテリアルスロットを損することがある(実測: Shinano で
        /// 3件 → 5件にすると SMR 3→4、MatSlots 7→9 でランクが Good から Medium へ落ちた)。
        ///
        /// 実測の残存面積: 問題の衣装は Shinano ドレス 4121cm²・セーター 4181cm²・
        /// スカート 3967cm²、Selena スカート 1365cm²・セーラー 1300cm²、
        /// Hanka コート 4432cm²、Velno コート 9688cm²。
        /// 一方、回す必要のない小物は Other_ear 137cm²、Body_hand 99cm²、
        /// under_shorts 40cm²、HairMekakure 13cm²。間が広く空いている。
        /// </summary>
        private const float StaticResidualAreaM2 = 0.05f; // 500cm² = 22cm 角

        /// <summary>
        /// ポーズ時の面積で軸方式へ切り替える判定。誤差程度の差で既定から動かしたくないので、
        /// 明確に小さいときだけ切り替える。実測ではタイツ 699→355、スラックス 1536→453 と
        /// 半分以下になるので、この閾値で十分捕まる。
        /// </summary>
        private const float AxisWinRatio = 0.9f;

        /// <summary>
        /// 軸方式を検討する下限。関節寄せのポーズ時残存がこれ未満なら既に消えているので、
        /// 相対比だけで判断すると誤差で既定から動いてしまう(実測: MUMUS の獣耳が
        /// 2.5% 対 2.2% で軸へ倒れた)。ポーズ依存型の実測値は Shinano タイツ 15%、
        /// Velno スラックス 11% なので、その下に置けば取りこぼさない。
        /// </summary>
        private const float AxisRelevantRatio = 0.05f;

        internal sealed class Advice
        {
            public ToggleCandidate Candidate;
            public HideMethod Method;
            public float RestRatio;   // 静止時の残存面積 / 元の面積
            public float PosedRatio;  // ポーズ時の残存面積 / 元の面積
            public string Reason;
        }

        /// <summary>
        /// 候補を解析して機構を割り当てる。ポーズを付けて測るため一時的にボーンを回すが、
        /// 必ず元へ戻す。
        /// </summary>
        public static List<Advice> Analyze(Transform root, IReadOnlyList<ToggleCandidate> candidates)
        {
            // 畳んだ頂点位置は現在のポーズに依らない(バインド空間で計算する)ので先に作る
            var collapsed = new Dictionary<SkinnedMeshRenderer, (Vector3[] joint, Vector3[] axis)>();
            foreach (var renderer in candidates.SelectMany(c => c.Renderers).Distinct())
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                collapsed[renderer] = (Collapse(renderer, false), Collapse(renderer, true));
            }

            var orig = new Dictionary<SkinnedMeshRenderer, float>();
            var restJoint = new Dictionary<SkinnedMeshRenderer, float>();
            var restAxis = new Dictionary<SkinnedMeshRenderer, float>();
            foreach (var (renderer, target) in collapsed)
            {
                orig[renderer] = SkinnedArea(renderer, null);
                restJoint[renderer] = SkinnedArea(renderer, target.joint);
                restAxis[renderer] = SkinnedArea(renderer, target.axis);
            }

            var posedJoint = new Dictionary<SkinnedMeshRenderer, float>();
            var posedAxis = new Dictionary<SkinnedMeshRenderer, float>();
            using (new TemporaryPose(root))
            {
                foreach (var (renderer, target) in collapsed)
                {
                    posedJoint[renderer] = SkinnedArea(renderer, target.joint);
                    posedAxis[renderer] = SkinnedArea(renderer, target.axis);
                }
            }

            var result = new List<Advice>();
            foreach (var candidate in candidates)
            {
                var total = candidate.Renderers.Sum(r => orig.GetValueOrDefault(r));
                if (total <= 0f)
                {
                    result.Add(new Advice
                    {
                        Candidate = candidate,
                        Method = HideMethod.BlendShape,
                        Reason = "面積を測れないため既定のまま",
                    });
                    continue;
                }

                float Ratio(Dictionary<SkinnedMeshRenderer, float> source) =>
                    candidate.Renderers.Sum(r => source.GetValueOrDefault(r)) / total;

                var restArea = Mathf.Min(
                    candidate.Renderers.Sum(r => restJoint.GetValueOrDefault(r)),
                    candidate.Renderers.Sum(r => restAxis.GetValueOrDefault(r)));
                var rest = Mathf.Min(Ratio(restJoint), Ratio(restAxis));
                var useAxis = Ratio(posedJoint) > AxisRelevantRatio
                              && Ratio(posedAxis) < Ratio(posedJoint) * AxisWinRatio;
                var posed = useAxis ? Ratio(posedAxis) : Ratio(posedJoint);

                var advice = new Advice
                {
                    Candidate = candidate,
                    RestRatio = rest,
                    PosedRatio = posed,
                };

                if (rest > StaticResidualRatio && restArea > StaticResidualAreaM2)
                {
                    advice.Method = HideMethod.UVTileDiscard;
                    advice.Reason =
                        $"静止時に元の面積の {rest:P0}({restArea * 1e4f:F0}cm²)が残る(畳んでも隠れない)";
                }
                else if (useAxis)
                {
                    advice.Method = HideMethod.BlendShapeAxis;
                    advice.Reason = $"ポーズを付けると出てくるが軸吸着で {posed:P0} まで下がる";
                }
                else
                {
                    advice.Method = HideMethod.BlendShape;
                    advice.Reason = $"畳めば隠れる(ポーズ時 {posed:P0})";
                }

                result.Add(advice);
            }

            AssignTiles(result);
            return result;
        }

        /// <summary>
        /// UV タイル破棄はタイル数と適用条件で頭打ちになる。残存の大きいものから順に
        /// 枠を配り、あぶれたものと適用条件を満たさないものは NaNimation へ落とす。
        /// </summary>
        private static void AssignTiles(List<Advice> advices)
        {
            var wanted = advices
                .Where(a => a.Method == HideMethod.UVTileDiscard)
                .OrderByDescending(a => a.RestRatio)
                .ToList();

            var used = 0;
            foreach (var advice in wanted)
            {
                var applicable = advice.Candidate.Renderers.All(UVTileDiscardHider.CanApply);
                if (applicable && used < UVTileDiscardHider.UsableTileCount)
                {
                    used++;
                    continue;
                }

                advice.Method = HideMethod.NaNimation;
                advice.Reason += applicable
                    ? $"。UV タイルが上限 {UVTileDiscardHider.UsableTileCount} 枚を超えたため NaNimation"
                    : "。UV タイル破棄の条件(lilToon 系・UV3 未使用)を満たさないため NaNimation";
            }
        }

        private static Vector3[] Collapse(SkinnedMeshRenderer renderer, bool toAxis)
        {
            var mesh = renderer.sharedMesh;
            var vertices = mesh.vertices;
            var deltas = BlendShapeHider.ComputeDeltas(mesh, renderer, vertices, toAxis);
            for (var v = 0; v < vertices.Length; v++) vertices[v] += deltas[v];
            return vertices;
        }

        /// <summary>
        /// 頂点位置(バインド空間)を現在のポーズでスキニングし、三角形面積の総和を返す。
        /// null なら元の頂点。
        /// </summary>
        private static float SkinnedArea(SkinnedMeshRenderer renderer, Vector3[] positions)
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null) return 0f;

            var source = positions ?? mesh.vertices;
            var bindposes = mesh.bindposes;
            var bones = renderer.bones;
            if (bindposes.Length == 0) return 0f;

            var skinning = new Matrix4x4[bindposes.Length];
            for (var i = 0; i < bindposes.Length; i++)
            {
                var bone = i < bones.Length ? bones[i] : null;
                skinning[i] = bone == null ? Matrix4x4.zero : bone.localToWorldMatrix * bindposes[i];
            }

            var bonesPerVertex = mesh.GetBonesPerVertex().ToArray();
            var weights = mesh.GetAllBoneWeights().ToArray();
            var skinned = new Vector3[source.Length];

            for (int v = 0, w = 0; v < source.Length; v++)
            {
                var count = v < bonesPerVertex.Length ? bonesPerVertex[v] : 0;
                var acc = Vector3.zero;
                var total = 0f;
                for (var i = 0; i < count; i++)
                {
                    var bw = weights[w + i];
                    if (bw.boneIndex < 0 || bw.boneIndex >= skinning.Length) continue;
                    acc += skinning[bw.boneIndex].MultiplyPoint3x4(source[v]) * bw.weight;
                    total += bw.weight;
                }

                w += count;
                skinned[v] = total > 0f ? acc / total : source[v];
            }

            var area = 0f;
            var triangles = mesh.triangles;
            for (var t = 0; t + 2 < triangles.Length; t += 3)
            {
                area += 0.5f * Vector3.Cross(
                    skinned[triangles[t + 1]] - skinned[triangles[t]],
                    skinned[triangles[t + 2]] - skinned[triangles[t]]).magnitude;
            }

            return area;
        }

        /// <summary>
        /// 着座に近いポーズを一時的に付ける。股・膝・肩・肘を曲げて関節跨ぎの破綻を出させ、
        /// Dispose で必ず元の回転へ戻す。
        /// </summary>
        private sealed class TemporaryPose : System.IDisposable
        {
            private readonly List<(Transform bone, Quaternion rotation)> _saved = new();

            public TemporaryPose(Transform root)
            {
                var animator = root.GetComponent<Animator>();
                if (animator == null || !animator.isHuman) return;

                void Bend(HumanBodyBones bone, Vector3 euler)
                {
                    var t = animator.GetBoneTransform(bone);
                    if (t == null) return;
                    _saved.Add((t, t.localRotation));
                    t.localRotation *= Quaternion.Euler(euler);
                }

                Bend(HumanBodyBones.LeftUpperLeg, new Vector3(-85, 0, 0));
                Bend(HumanBodyBones.RightUpperLeg, new Vector3(-85, 0, 0));
                Bend(HumanBodyBones.LeftLowerLeg, new Vector3(95, 0, 0));
                Bend(HumanBodyBones.RightLowerLeg, new Vector3(95, 0, 0));
                Bend(HumanBodyBones.LeftUpperArm, new Vector3(0, 0, 60));
                Bend(HumanBodyBones.RightUpperArm, new Vector3(0, 0, -60));
                Bend(HumanBodyBones.LeftLowerArm, new Vector3(0, -70, 0));
                Bend(HumanBodyBones.RightLowerArm, new Vector3(0, 70, 0));
            }

            public void Dispose()
            {
                foreach (var (bone, rotation) in _saved)
                    if (bone != null) bone.localRotation = rotation;
            }
        }
    }
}
