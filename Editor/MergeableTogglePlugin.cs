using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;

[assembly: ExportsPlugin(typeof(Kie.MergeableToggle.Editor.MergeableTogglePlugin))]

namespace Kie.MergeableToggle.Editor
{
    internal sealed class MergeableTogglePlugin : Plugin<MergeableTogglePlugin>
    {
        public override string QualifiedName => "com.kie.kie-mergeable-toggle";
        public override string DisplayName => "Mergeable Toggle";

        protected override void Configure()
        {
            // MA より後に走らせる。MA Merge Animator が合流させたコントローラも、MA が
            // リアクティブに生成したトグルも、合流後の仮想アニメーター上では手書きトグルと
            // 区別が付かないので、そのまま候補になる。
            //
            // 以前は「手書きレイヤーの状態のまま処理したい」として MA より前に走らせていたが、
            // それだと Merge Animator 経由のトグルが一切見えず、そういう構成のアバターで
            // 何も変換されなかった(実測: FX を Merge Animator へ移すと検出 9 → 0)。
            //
            // 変換本体は AnimationIndex 経由で候補抽出とカーブ書き換えを行い、descriptor を
            // 直接見ていないので、順序を後ろへ動かしても手を入れる箇所は無い。
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq =>
                    seq.Run("Convert mesh toggles", ToggleConverter.Convert));
        }
    }

    /// <summary>
    /// m_IsActive トグルを、選択された隠蔽機構へ変換する。
    ///
    /// 共通層の役割:
    /// - 候補の抽出と絞り込み
    /// - 対象を常時アクティブ化し、元の m_IsActive カーブをバックエンドの
    ///   バインディングへ書き換える
    /// - rootBone / localBounds / updateWhenOffscreen を正規化し、AAO の
    ///   AutoMergeSkinnedMesh の CategorizationKey を揃える(統合を可能にする本体)
    ///
    /// 機構ごとの差分は <see cref="HidePlan"/> を返すバックエンドに閉じている。
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
                var candidates = ToggleScanner.ScanHierarchy(root.transform, path =>
                        asc.AnimationIndex.GetClipsForBinding(
                            EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive")).Any(),
                        component.disableComponentsWhenHidden)
                    .Where(c => c.IsClean ? !excluded.Contains(c.Path) : forced.Contains(c.Path))
                    .Where(c => c.Renderers.All(r => CanApply(component.MethodFor(c.Path), r)))
                    .ToList();

                // 入れ子トグルで同じレンダラーが二重に変換されると、ブレンドシェイプが
                // 二重に効く(デルタが加算されて原点を通り越す)等の破綻が起きる。
                // 外側から順に確保し、既に確保済みのレンダラーを含む候補は落とす。
                var claimed = new HashSet<SkinnedMeshRenderer>();
                var targets = new List<ToggleCandidate>();
                foreach (var candidate in candidates.OrderBy(c => c.Path.Count(ch => ch == '/')))
                {
                    if (candidate.Renderers.Any(claimed.Contains))
                    {
                        Debug.LogWarning(
                            $"[MergeableToggle] skipped nested toggle '{candidate.Path}' " +
                            "(its renderers are already converted by an outer toggle)");
                        continue;
                    }

                    foreach (var renderer in candidate.Renderers) claimed.Add(renderer);
                    targets.Add(candidate);
                }

                // インスペクタの一覧はビルド結果と一致しない(ビルド時にトグルを生成する
                // ツールのぶんは編集時に存在しない)。一覧は参考表示で、正はこのログ。
                Debug.Log($"[MergeableToggle] converting {targets.Count} toggles\n" +
                          string.Join("\n", targets.Select(
                              t => $"  {component.MethodFor(t.Path)}\t{t.Path}")));
                if (targets.Count == 0) return;

                // 正規化用の共通 rootBone と合併バウンズ(変換前の値で計算)
                var animator = root.GetComponent<Animator>();
                var commonRootBone =
                    (animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null)
                    ?? root.transform;
                var unionBounds = ComputeUnionBounds(
                    targets.SelectMany(t => t.Renderers).Distinct(), commonRootBone);

                // UV タイル破棄はタイルを奪い合うので先に配る。
                // 使い切ったトグルは対象から落とす(方式を変えてもらう)。
                var tileOf = new Dictionary<string, int>();
                foreach (var target in targets.Where(t => component.MethodFor(t.Path) == HideMethod.UVTileDiscard))
                {
                    if (tileOf.Count >= UVTileDiscardHider.UsableTileCount)
                    {
                        Debug.LogWarning(
                            $"[MergeableToggle] '{target.Path}' は UV タイルを使い切ったため " +
                            $"({UVTileDiscardHider.UsableTileCount} 枚まで)変換しません。" +
                            "一部のトグルを別の方式へ切り替えてください。");
                        continue;
                    }

                    tileOf[target.Path] = tileOf.Count;
                }

                targets.RemoveAll(t => component.MethodFor(t.Path) == HideMethod.UVTileDiscard
                                       && !tileOf.ContainsKey(t.Path));

                var tileRenderers = targets
                    .Where(t => tileOf.ContainsKey(t.Path))
                    .SelectMany(t => t.Renderers)
                    .Distinct()
                    .ToList();

                foreach (var target in targets)
                {
                    var initiallyHidden = !target.Object.activeSelf;
                    var method = component.MethodFor(target.Path);

                    if (method == HideMethod.UVTileDiscard && initiallyHidden
                        && !component.skipInitiallyHiddenMaterialClone)
                        UVTileDiscardHider.SetInitiallyHidden(target, tileOf[target.Path]);

                    var plan = Apply(method, target, root.transform, initiallyHidden,
                        tileOf.TryGetValue(target.Path, out var tile) ? tile : 0, tileRenderers);
                    if (plan.IsEmpty)
                    {
                        Debug.LogWarning($"[MergeableToggle] '{target.Path}' produced no hide plan; left as-is");
                        continue;
                    }

                    if (component.disableComponentsWhenHidden)
                        ComponentDisabler.AddDisableBindings(target, root.transform, plan);

                    target.Object.SetActive(true);
                    RewriteToggleCurves(asc, target.Path, plan);
                }

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

        internal static bool CanApply(HideMethod method, SkinnedMeshRenderer renderer)
        {
            return method switch
            {
                HideMethod.NaNimation => NaNimationHider.CanApply(renderer),
                HideMethod.UVTileDiscard => UVTileDiscardHider.CanApply(renderer),
                _ => BlendShapeHider.CanApply(renderer),
            };
        }

        private static HidePlan Apply(
            HideMethod method, ToggleCandidate target, Transform root, bool initiallyHidden, int tileIndex,
            List<SkinnedMeshRenderer> tileRenderers)
        {
            return method switch
            {
                HideMethod.NaNimation => NaNimationHider.Apply(target, root, initiallyHidden),
                HideMethod.UVTileDiscard =>
                    UVTileDiscardHider.Apply(target, root, initiallyHidden, tileIndex, tileRenderers),
                _ => BlendShapeHider.Apply(
                    target, root, initiallyHidden, method == HideMethod.BlendShapeAxis),
            };
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
        /// oldPath の m_IsActive カーブを、計画のバインディングへ書き換える。
        /// アクティブ→visible / 非アクティブ→hidden。
        /// </summary>
        private static void RewriteToggleCurves(AnimatorServicesContext asc, string oldPath, HidePlan plan)
        {
            var oldBinding = EditorCurveBinding.FloatCurve(oldPath, typeof(GameObject), "m_IsActive");

            asc.AnimationIndex.EditClipsByBinding(new[] { oldBinding }, clip =>
            {
                var curve = clip.GetFloatCurve(oldBinding);
                if (curve == null) return;

                foreach (var (binding, visible, hidden) in plan.Toggled)
                {
                    // AnimationCurve のコンストラクタは NaN 値のキーを黙って捨てるため、
                    // MA と同様に AddKey + オブジェクト初期化子で構築する
                    var mapped = new AnimationCurve();
                    foreach (var key in curve.keys)
                    {
                        mapped.AddKey(new Keyframe(key.time, 0)
                        {
                            value = key.value >= 0.5f ? visible : hidden,
                        });
                    }

                    clip.SetFloatCurve(binding, mapped);
                }

                foreach (var (binding, value) in plan.Constant)
                    clip.SetFloatCurve(binding, AnimationCurve.Constant(0, 1, value));

                clip.SetFloatCurve(oldBinding, null);
            });
        }
    }
}
