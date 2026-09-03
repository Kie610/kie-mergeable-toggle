using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

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

            // AAO の自動統合は PPtr アニメーションを持つレンダラーを統合しないので、
            // マテリアルの差し替えは統合の後に足す。
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin("com.anatawa12.avatar-optimizer")
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq =>
                    seq.Run("Empty hidden material slots", HiddenSlotPass.Run));
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
    /// 隠しかたの実装は <see cref="HidePlan"/> を返す <see cref="InfinimationHider"/> に閉じている。
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
                System.Func<EditorCurveBinding, bool> isEnabledAlreadyAnimated =
                    binding => asc.AnimationIndex.GetClipsForBinding(binding).Any();
                var candidates = ToggleScanner.ScanHierarchy(root.transform, path =>
                        asc.AnimationIndex.GetClipsForBinding(
                            EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive")).Any(),
                        isEnabledAlreadyAnimated,
                        component.disableComponentsWhenHidden)
                    .Where(c => c.IsClean ? !excluded.Contains(c.Path) : forced.Contains(c.Path))
                    .Where(c => c.Renderers.All(InfinimationHider.CanApply))
                    .ToList();

                // 入れ子トグルで同じレンダラーが二重に変換されると、ブレンドシェイプが
                // 二重に効く(デルタが加算される)等の破綻が起きる。
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
                          string.Join("\n", targets.Select(t => "  " + t.Path)));
                if (targets.Count == 0) return;

                // 正規化用の共通 rootBone と合併バウンズ(変換前の値で計算)。
                // アバター内に既に rootBone の合意が成立している場合 (MA Mesh Settings 等)、
                // Hips へ固定するとその合意から外れて AAO の統合グループを割る (CustomBase で
                // 実測)。変換対象以外の SMR の最頻値へ合わせ、無ければ Hips へ倒す。
                var animator = root.GetComponent<Animator>();
                var convertedRenderers = new HashSet<SkinnedMeshRenderer>(
                    targets.SelectMany(t => t.Renderers));
                var consensusRootBone = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => !convertedRenderers.Contains(r) && r.sharedMesh != null && r.rootBone != null)
                    .GroupBy(r => r.rootBone)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => AnimationUtility.CalculateTransformPath(g.Key, root.transform))
                    .FirstOrDefault()?.Key;
                var commonRootBone = consensusRootBone
                    ?? (animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null)
                    ?? root.transform;
                var initiallyHiddenByTarget = targets.ToDictionary(
                    target => target, target => !target.Object.activeSelf);
                var plans = new Dictionary<ToggleCandidate, HidePlan>();
                var hiddenRenderersByTarget = new Dictionary<ToggleCandidate, HashSet<Renderer>>();

                // PB の帰属判定より先に、実際に生成できる隠蔽・無効化バインディングを確定する。
                foreach (var target in targets)
                {
                    var plan = InfinimationHider.Apply(
                        target, root.transform, initiallyHiddenByTarget[target]);
                    if (plan.IsEmpty)
                    {
                        Debug.LogWarning($"[MergeableToggle] '{target.Path}' produced no hide plan; left as-is");
                        continue;
                    }

                    var hiddenRenderers = new HashSet<Renderer>(target.Renderers);
                    if (component.disableComponentsWhenHidden)
                    {
                        hiddenRenderers.UnionWith(ComponentDisabler.AddDisableBindings(
                            target, root.transform, plan, isEnabledAlreadyAnimated,
                            initiallyHiddenByTarget[target]));
                    }

                    plans[target] = plan;
                    hiddenRenderersByTarget[target] = hiddenRenderers;
                }

                // 隠蔽計画を作れなかった候補は変換していないので、正規化からも外す。
                // targets のままにすると、変換していないレンダラーの rootBone /
                // localBounds / updateWhenOffscreen まで書き換えてしまう。
                var effectiveTargets = targets.Where(plans.ContainsKey).ToList();
                var normalizedRenderers = effectiveTargets
                    .SelectMany(t => t.Renderers).Distinct().ToList();
                var unionBounds = ComputeUnionBounds(normalizedRenderers, commonRootBone);

                var pbStopper = component.disablePhysBonesWhenHidden
                    ? PhysBoneStopper.Build(root.transform)
                    : null;
                var pbClassification = pbStopper?.Classify(
                    effectiveTargets, hiddenRenderersByTarget, isEnabledAlreadyAnimated);
                // AAP `MT_Hidden/<パス>` は共有 PB の停止と隠れたスロットの差し替えの両方が
                // AND ゲートの条件に使う。どちらかが有効なら、FX に作れる全トグルへ作る。
                var needParameters = (pbStopper != null && component.disableSharedPhysBonesWhenHidden)
                                     || component.emptyHiddenMaterialSlots;
                var parameterOwners = needParameters
                    ? OwnersInFx(asc, effectiveTargets)
                    : new HashSet<ToggleCandidate>();
                var sharedGroups = new List<PhysBoneStopper.SharedGroup>();
                if (pbStopper != null && component.disableSharedPhysBonesWhenHidden)
                {
                    foreach (var group in pbClassification.Shared)
                    {
                        var missing = group.Owners.Where(owner => !parameterOwners.Contains(owner)).ToList();
                        if (missing.Count > 0)
                        {
                            Debug.LogWarning(
                                "[MergeableToggle] skipped shared PhysBone group because owners have no FX parameter: " +
                                string.Join(", ", missing.Select(owner => owner.Path)));
                            continue;
                        }
                        sharedGroups.Add(group);
                    }
                }
                var sharedParameters = parameterOwners.ToDictionary(
                    target => target,
                    SharedParameterName);

                VirtualAnimatorController fx = null;
                if (parameterOwners.Count > 0)
                {
                    fx = asc.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX];
                    foreach (var owner in parameterOwners)
                    {
                        fx.SetParameter(sharedParameters[owner], new AnimatorControllerParameter
                        {
                            name = sharedParameters[owner],
                            type = AnimatorControllerParameterType.Float,
                            defaultFloat = initiallyHiddenByTarget[owner] ? 1f : 0f,
                        });
                    }
                }
                var stoppedLog = new List<string>();

                // 第 2 パス (AAO の後) がスロットの所有トグルを引けるよう、隠蔽シェイプ名を控える
                var slotState = context.GetState<HiddenSlotState>();
                slotState.Enabled = component.emptyHiddenMaterialSlots;
                foreach (var target in effectiveTargets)
                {
                    foreach (var (binding, _, _) in plans[target].Toggled)
                    {
                        if (binding.type != typeof(SkinnedMeshRenderer) ||
                            !binding.propertyName.StartsWith("blendShape.MT_Hide_")) continue;
                        slotState.ByShape[binding.propertyName.Substring("blendShape.".Length)] =
                            new HiddenSlotState.ToggleInfo
                            {
                                Path = target.Path,
                                InitiallyHidden = initiallyHiddenByTarget[target],
                                Parameter = sharedParameters.TryGetValue(target, out var p) ? p : null,
                            };
                    }
                }

                foreach (var target in effectiveTargets)
                {
                    var plan = plans[target];

                    if (pbStopper != null)
                    {
                        foreach (var (path, binding, physBone) in pbClassification.Exclusive[target])
                        {
                            plan.Toggled.Add((binding, 1f, 0f));
                            if (initiallyHiddenByTarget[target]) physBone.enabled = false;
                            stoppedLog.Add($"  {target.Path} -> {path}");
                        }

                    }

                    if (sharedParameters.TryGetValue(target, out var parameter))
                    {
                        plan.Toggled.Add((EditorCurveBinding.FloatCurve(
                            "", typeof(Animator), parameter), 0f, 1f));
                    }

                    target.Object.SetActive(true);
                    RewriteToggleCurves(asc, target.Path, plan);
                }

                if (sharedGroups.Count > 0)
                    AddSharedPhysBoneLayers(
                        fx, sharedGroups, sharedParameters, initiallyHiddenByTarget, stoppedLog);

                // 一覧(編集時)はビルド結果と一致しないので、何を止めたかはこのログが正
                if (pbStopper != null)
                    Debug.Log($"[MergeableToggle] stopping " +
                              $"{pbClassification.Exclusive.Sum(kv => kv.Value.Count) + sharedGroups.Sum(g => g.PhysBones.Count)} " +
                              "armature-side PhysBones while hidden\n" +
                              string.Join("\n", stoppedLog));

                foreach (var renderer in normalizedRenderers)
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

        /// <summary>
        /// AAP `MT_Hidden/&lt;パス&gt;` を作れるトグルを返す。AAP は書き込まれたコントローラ内でしか
        /// 値を駆動できないため、m_IsActive を持つ全クリップが FX に属し、かつ他の
        /// コントローラから届かないトグルに限る。
        /// </summary>
        private static HashSet<ToggleCandidate> OwnersInFx(
            AnimatorServicesContext asc, IEnumerable<ToggleCandidate> targets)
        {
            var accepted = new HashSet<ToggleCandidate>();
            if (!asc.ControllerContext.Controllers.TryGetValue(
                    VRCAvatarDescriptor.AnimLayerType.FX, out var fx))
            {
                Debug.LogWarning("[MergeableToggle] FX controller is missing; no MT_Hidden parameter is created");
                return accepted;
            }

            var fxClips = new HashSet<VirtualClip>(fx.AllReachableNodes().OfType<VirtualClip>());
            var reachableOutsideFx = new HashSet<VirtualClip>(
                asc.ControllerContext.Controllers
                    .Where(controller => !Equals(
                        controller.Key, VRCAvatarDescriptor.AnimLayerType.FX))
                    .SelectMany(controller => controller.Value.AllReachableNodes().OfType<VirtualClip>()));
            foreach (var owner in targets)
            {
                var binding = EditorCurveBinding.FloatCurve(
                    owner.Path, typeof(GameObject), "m_IsActive");
                var clips = asc.AnimationIndex.GetClipsForBinding(binding).ToList();
                string reason = null;
                if (clips.Count == 0 || clips.Any(clip => !fxClips.Contains(clip)))
                    reason = "toggle clips are not all in FX";
                else if (clips.Any(reachableOutsideFx.Contains))
                    reason = "a toggle clip is reachable from a non-FX controller";
                else if (fx.Parameters.ContainsKey(SharedParameterName(owner)))
                    reason = "FX parameter already exists";

                if (reason != null)
                {
                    Debug.LogWarning($"[MergeableToggle] no MT_Hidden parameter for '{owner.Path}': {reason}");
                    continue;
                }

                accepted.Add(owner);
            }

            return accepted;
        }

        private static string SharedParameterName(ToggleCandidate target)
        {
            return "MT_Hidden/" + target.Path;
        }

        private static void AddSharedPhysBoneLayers(
            VirtualAnimatorController fx,
            IReadOnlyList<PhysBoneStopper.SharedGroup> groups,
            IReadOnlyDictionary<ToggleCandidate, string> parameters,
            IReadOnlyDictionary<ToggleCandidate, bool> initiallyHiddenByTarget,
            ICollection<string> stoppedLog)
        {
            for (var index = 0; index < groups.Count; index++)
            {
                var group = groups[index];
                var layerName = $"MT_PBStop {index + 1}";
                AndGateLayer.Add(fx, layerName,
                    group.Owners.Select(owner => parameters[owner]).ToList(),
                    group.Owners.All(owner => initiallyHiddenByTarget[owner]),
                    active =>
                    {
                        foreach (var (_, binding) in group.PhysBones)
                            active.SetFloatCurve(binding, SingleKeyCurve(1f));
                    },
                    stopped =>
                    {
                        foreach (var (_, binding) in group.PhysBones)
                            stopped.SetFloatCurve(binding, SingleKeyCurve(0f));
                    });

                stoppedLog.Add($"  {layerName} -> owners: {string.Join(", ", group.Owners.Select(owner => owner.Path))}");
                stoppedLog.Add($"  {layerName} -> PhysBones: {string.Join(", ", group.PhysBones.Select(pb => pb.path))}");
            }
        }

        private static AnimationCurve SingleKeyCurve(float value)
        {
            var curve = new AnimationCurve();
            curve.AddKey(new Keyframe(0f, value));
            return curve;
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
                            inTangent = float.PositiveInfinity,
                            outTangent = float.PositiveInfinity,
                        });
                    }

                    clip.SetFloatCurve(binding, mapped);
                }

                clip.SetFloatCurve(oldBinding, null);
            });
        }
    }
}
