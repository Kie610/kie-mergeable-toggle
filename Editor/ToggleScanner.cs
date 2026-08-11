using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>アニメーターで m_IsActive トグルされている、SMR を含むオブジェクト。</summary>
    public sealed class ToggleCandidate
    {
        /// <summary>アバタールートからの相対パス</summary>
        public string Path;

        /// <summary>解決済みオブジェクト</summary>
        public GameObject Object;

        /// <summary>サブツリー内の統合対象 SkinnedMeshRenderer(非アクティブ含む)</summary>
        public List<SkinnedMeshRenderer> Renderers = new();

        /// <summary>トグルを含むクリップ名(表示用)</summary>
        public List<string> SourceClips = new();

        /// <summary>SMR/Transform/IEditorOnly 以外のコンポーネント型名。空ならクリーン候補</summary>
        public List<string> Warnings = new();

        public bool IsClean => Warnings.Count == 0;
    }

    public static class ToggleScanner
    {
        /// <summary>
        /// アバターの全 Playable Layer から m_IsActive トグルを検出し、
        /// サブツリーに SMR を持つものを候補として返す(パス順)。
        /// </summary>
        public static List<ToggleCandidate> Scan(VRCAvatarDescriptor descriptor, bool componentsWillBeDisabled = true)
        {
            var root = descriptor.transform;
            var pathToClips = new Dictionary<string, List<string>>();

            foreach (var (clip, prefix) in EnumerateClips(descriptor))
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive") continue;

                    var path = prefix + binding.path;
                    if (string.IsNullOrEmpty(path)) continue; // ルート自身は対象外

                    if (!pathToClips.TryGetValue(path, out var clips))
                        pathToClips[path] = clips = new List<string>();
                    if (!clips.Contains(clip.name)) clips.Add(clip.name);
                }
            }

            var result = new List<ToggleCandidate>();
            foreach (var (path, clips) in pathToClips.OrderBy(kv => kv.Key))
            {
                var target = root.Find(path);
                if (target == null) continue;

                var renderers = target.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r.sharedMesh != null)
                    .ToList();
                if (renderers.Count == 0) continue;

                result.Add(new ToggleCandidate
                {
                    Path = path,
                    Object = target.gameObject,
                    Renderers = renderers,
                    SourceClips = clips,
                    Warnings = CollectWarnings(target, componentsWillBeDisabled),
                });
            }

            return result;
        }

        /// <summary>
        /// ビルド時用: アニメーターが仮想化された後でも動くよう、階層側から
        /// 「m_IsActive がアニメーションされているか」を問い合わせて候補を組み立てる。
        /// </summary>
        public static List<ToggleCandidate> ScanHierarchy(
            Transform root, System.Func<string, bool> isActivenessAnimated, bool componentsWillBeDisabled = true)
        {
            var result = new List<ToggleCandidate>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == root) continue;

                var path = AnimationUtility.CalculateTransformPath(transform, root);
                if (!isActivenessAnimated(path)) continue;

                var renderers = transform.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r.sharedMesh != null)
                    .ToList();
                if (renderers.Count == 0) continue;

                result.Add(new ToggleCandidate
                {
                    Path = path,
                    Object = transform.gameObject,
                    Renderers = renderers,
                    Warnings = CollectWarnings(transform, componentsWillBeDisabled),
                });
            }

            return result;
        }

        /// <summary>
        /// 編集時に見えるクリップと、そのカーブパスに前置すべき接頭辞を返す。
        ///
        /// ビルド時は MA より後に走るので合流後のアニメーターが全部見えるが、編集時には
        /// まだ合流していない。ディスクリプタの Playable Layer だけを見ると
        /// MA Merge Animator 経由のトグルが一覧に出ないので、ここでも辿る。
        ///
        /// MA がリアクティブに生成するトグル (Object Toggle 等) は編集時には存在しないため
        /// 一覧に出せない。乖離の向きは常に「ビルド ⊇ 表示」なので、
        /// 「表示されるのに変換されない」は起きない。
        /// </summary>
        private static IEnumerable<(AnimationClip clip, string prefix)> EnumerateClips(
            VRCAvatarDescriptor descriptor)
        {
            var seen = new HashSet<(AnimationClip, string)>();
            var layers = (descriptor.baseAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                .Concat(descriptor.specialAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>());

            foreach (var layer in layers)
            {
                if (layer.isDefault || layer.animatorController == null) continue;
                foreach (var clip in layer.animatorController.animationClips)
                    if (clip != null && seen.Add((clip, ""))) yield return (clip, "");
            }

#if MT_MA_PRESENT
            foreach (var merge in descriptor
                         .GetComponentsInChildren<nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator>(true))
            {
                if (merge == null || !merge.enabled || merge.animator == null) continue;

                // Relative はコンポーネント(または relativePathRoot)基準のパスなので、
                // アバタールート相対へ直してから照合する。
                var prefix = "";
                if (merge.pathMode == nadena.dev.modular_avatar.core.MergeAnimatorPathMode.Relative)
                {
                    var basis = merge.relativePathRoot?.Get(merge) ?? merge.gameObject;
                    var basisPath = AnimationUtility.CalculateTransformPath(
                        basis.transform, descriptor.transform);
                    if (!string.IsNullOrEmpty(basisPath)) prefix = basisPath + "/";
                }

                foreach (var clip in merge.animator.animationClips)
                    if (clip != null && seen.Add((clip, prefix))) yield return (clip, prefix);
            }
#endif
        }

        /// <summary>
        /// トグルを常時アクティブ化しても挙動が変わらないか検査する。
        /// 残ってしまうコンポーネントの型名を警告として返す。
        ///
        /// コンポーネント無効化が有効なら、m_Enabled を持つものは非表示中に一緒に
        /// 落ちるので警告にしない。落とせないものだけが残る。
        /// </summary>
        private static List<string> CollectWarnings(Transform target, bool componentsWillBeDisabled)
        {
            var warnings = new List<string>();
            foreach (var component in target.GetComponentsInChildren<Component>(true))
            {
                switch (component)
                {
                    case null:
                        if (!warnings.Contains("Missing Script")) warnings.Add("Missing Script");
                        continue;
                    case Transform:
                    case SkinnedMeshRenderer:
                    case IEditorOnly:
                        continue;
                    default:
                        if (componentsWillBeDisabled && ComponentDisabler.CanDisable(component)) continue;
                        var name = component.GetType().Name;
                        if (!warnings.Contains(name)) warnings.Add(name);
                        continue;
                }
            }

            return warnings;
        }
    }
}
