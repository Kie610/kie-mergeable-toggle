using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

[assembly: ExportsPlugin(typeof(Kie.MergeableToggle.Editor.MergeableTogglePlugin))]

namespace Kie.MergeableToggle.Editor
{
    internal sealed class MergeableTogglePlugin : Plugin<MergeableTogglePlugin>
    {
        public override string QualifiedName => "com.kie.mergeable-toggle";
        public override string DisplayName => "Mergeable Toggle";

        protected override void Configure()
        {
            // 手書きレイヤーの状態のまま処理したいので MA(リアクティブ生成)より前に走らせる
            InPhase(BuildPhase.Transforming)
                .BeforePlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq =>
                    seq.Run("Convert toggles to NaNimation", ToggleConverter.Convert));
        }
    }

    /// <summary>
    /// m_IsActive トグルを NaNimation(ボーンスケール NaN⇔1)へ変換する。
    ///
    /// - 対象メッシュは常時アクティブ化され、表示切替は複製ボーンのスケールが担う。
    ///   全頂点が同時に隠れるため頂点複製は不要(貪欲カバーで選んだボーンの複製と
    ///   ウェイト付替のみ)。
    /// - rootBone / localBounds / updateWhenOffscreen を正規化し、AAO の
    ///   AutoMergeSkinnedMesh の CategorizationKey が揃うようにする(これが
    ///   メッシュ統合を可能にする本体)。
    /// - 初期非表示は NaN をシリアライズできないため VRCScaleConstraint
    ///   (Source weight = NaN)で代用し、トグルクリップ側で無効化する。
    /// </summary>
    internal static class ToggleConverter
    {
        public static void Convert(BuildContext context)
        {
            var root = context.AvatarRootObject;
            var component = root.GetComponent<MergeableToggle>();
            if (component == null) return;

            try
            {
                if (!component.enableConversion) return;

                var excluded = new HashSet<string>(component.excludedPaths ?? new List<string>());
                var forced = new HashSet<string>(component.forceIncludedPaths ?? new List<string>());

                // アニメーターは既に仮想化されているため、AnimationIndex へ問い合わせて候補を出す
                var asc = context.Extension<AnimatorServicesContext>();
                var targets = ToggleScanner.ScanHierarchy(root.transform, path =>
                        asc.AnimationIndex.GetClipsForBinding(
                            EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive")).Any())
                    .Where(c => c.IsClean ? !excluded.Contains(c.Path) : forced.Contains(c.Path))
                    .Where(c => c.Renderers.All(CanApplyNaNimation))
                    .ToList();
                Debug.Log($"[MergeableToggle] converting {targets.Count} toggles");
                if (targets.Count == 0) return;

                // 正規化用の共通 rootBone と合併バウンズ(変換前の値で計算)
                var animator = root.GetComponent<Animator>();
                var commonRootBone =
                    (animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null)
                    ?? root.transform;
                var unionBounds = ComputeUnionBounds(
                    targets.SelectMany(t => t.Renderers).Distinct(), commonRootBone);

                foreach (var target in targets)
                {
                    var scaleBones = new List<Transform>();
                    foreach (var renderer in target.Renderers)
                        scaleBones.AddRange(ApplyNaNimation(renderer));

                    var initiallyHidden = !target.Object.activeSelf;
                    target.Object.SetActive(true);

                    if (initiallyHidden) AddInitialStateConstraints(scaleBones);
                    RewriteToggleCurves(asc, root.transform, target.Path, scaleBones, initiallyHidden);
                }

                // 正規化は全トグル処理後にまとめて行う(入れ子トグルで同一レンダラーを
                // 複数回処理しても壊れないよう、冪等な代入だけにする)
                foreach (var renderer in targets.SelectMany(t => t.Renderers).Distinct())
                {
                    renderer.rootBone = commonRootBone;
                    renderer.localBounds = unionBounds;
                    renderer.updateWhenOffscreen = false;
                }
            }
            finally
            {
                Object.DestroyImmediate(component);
            }
        }

        private static bool CanApplyNaNimation(SkinnedMeshRenderer renderer)
        {
            // ボーンウェイトを持たない SMR(クロス等)は NaN を届ける先がない
            return renderer.sharedMesh != null && renderer.sharedMesh.bindposeCount > 0
                                               && renderer.bones.Length > 0;
        }

        /// <summary>
        /// レンダラーの全頂点を NaN 化できるようにする。
        /// 貪欲セットカバーで「残り頂点を最も多くカバーするボーン」を選び、その複製を
        /// 選択ボーンの子として作成、カバーした頂点の該当ウェイトを複製へ付け替える。
        /// 複製は親と同一姿勢(ローカル恒等・bindpose コピー)なのでスケール 1 の間
        /// スキニングは不変。入れ子トグルで再変換された場合は前回の複製ボーンが
        /// 選ばれ、その子として新複製ができるため、親の NaN も伝播する(OR 条件)。
        /// </summary>
        private static List<Transform> ApplyNaNimation(SkinnedMeshRenderer renderer)
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

            // ボーンごとの被覆頂点数
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

        private static Bounds ComputeUnionBounds(IEnumerable<SkinnedMeshRenderer> renderers, Transform rootBone)
        {
            var union = new Bounds();
            var first = true;
            foreach (var renderer in renderers)
            {
                var source = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
                var toCommon = rootBone.worldToLocalMatrix * source.localToWorldMatrix;
                var b = renderer.localBounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var local = b.center + Vector3.Scale(b.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var point = toCommon.MultiplyPoint3x4(local);
                    if (first)
                    {
                        union = new Bounds(point, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        union.Encapsulate(point);
                    }
                }
            }

            return union;
        }

        /// <summary>
        /// 初期非表示トグル用: シーンに NaN スケールを保存できないため、
        /// VRCScaleConstraint(Source weight = NaN)で起動直後から NaN にする。
        /// アニメーション側が動き出したら RewriteToggleCurves の IsActive=0 で無効化される。
        /// </summary>
        private static void AddInitialStateConstraints(List<Transform> scaleBones)
        {
            foreach (var bone in scaleBones)
            {
                var constraint = bone.gameObject.AddComponent<VRCScaleConstraint>();
                constraint.Sources.Add(new VRCConstraintSource
                {
                    SourceTransform = constraint.transform,
                    Weight = float.NaN,
                });
                constraint.GlobalWeight = 1.0f;
                constraint.Locked = true;
                constraint.IsActive = true;
            }
        }

        /// <summary>
        /// oldPath の m_IsActive カーブを、全クリップでスケールボーンの
        /// m_LocalScale NaN⇔1 カーブへ書き換える(アクティブ→1 / 非アクティブ→NaN)。
        /// </summary>
        private static void RewriteToggleCurves(
            AnimatorServicesContext asc, Transform root, string oldPath,
            List<Transform> scaleBones, bool hasConstraint)
        {
            var oldBinding = EditorCurveBinding.FloatCurve(oldPath, typeof(GameObject), "m_IsActive");
            var bonePaths = scaleBones.Select(b => AnimationUtility.CalculateTransformPath(b, root)).ToList();

            asc.AnimationIndex.EditClipsByBinding(new[] { oldBinding }, clip =>
            {
                var curve = clip.GetFloatCurve(oldBinding);
                if (curve == null) return;

                // AnimationCurve のコンストラクタは NaN 値のキーを黙って捨てるため、
                // MA と同様に AddKey + オブジェクト初期化子で構築する
                var scaleCurve = new AnimationCurve();
                foreach (var key in curve.keys)
                {
                    scaleCurve.AddKey(new Keyframe(key.time, 0)
                    {
                        value = key.value >= 0.5f ? 1f : float.NaN,
                    });
                }

                foreach (var bonePath in bonePaths)
                {
                    foreach (var axis in new[] { "x", "y", "z" })
                    {
                        clip.SetFloatCurve(
                            EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), "m_LocalScale." + axis),
                            scaleCurve);
                    }

                    if (hasConstraint)
                    {
                        clip.SetFloatCurve(
                            EditorCurveBinding.FloatCurve(bonePath, typeof(VRCScaleConstraint), "IsActive"),
                            AnimationCurve.Constant(0, 1, 0f));
                    }
                }

                clip.SetFloatCurve(oldBinding, null);
            });
        }
    }
}
