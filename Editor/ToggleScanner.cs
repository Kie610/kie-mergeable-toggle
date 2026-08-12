using System;
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

        /// <summary>メニュー上の表示名。メニューから駆動されていなければ null</summary>
        public string Label;

        /// <summary>同じラベルの候補が他にもある。ラベルだけでは判別できない</summary>
        internal bool LabelIsAmbiguous;

        /// <summary>他の候補と区別が付く最短のパス末尾。ToggleScanner が割り当てる</summary>
        internal string Disambiguator;

        /// <summary>まとめて表示するグループの識別子(グループ根のパス)</summary>
        public string GroupKey;

        /// <summary>グループの見出し(プレハブ名か親オブジェクト名)</summary>
        public string GroupLabel;

        /// <summary>グループ根から見た相対パス。グループ内での行名に使う</summary>
        public string PathInGroup;

        public bool IsClean => Warnings.Count == 0;

        /// <summary>
        /// 一覧に出す名前。GameObject 名だけでは判別できない
        /// (実アバターで 'HandleMesh' が 6 個並ぶ) ので、メニュー名を優先する。
        ///
        /// ただし 1 つのパラメータが複数オブジェクトを駆動していると、メニュー名でも
        /// 同名が並ぶ (実測: APS のハンドル 6 件がすべて 'ShowHandle')。
        /// その場合と、そもそもラベルが無い場合は、区別が付くまで伸ばしたパス末尾を添える。
        /// 段数を固定にすると足りない (実測: ペンライトの左右が 3 段上でしか分かれない)。
        /// </summary>
        public string DisplayName
        {
            get
            {
                var tail = Disambiguator ?? Path;
                if (string.IsNullOrEmpty(Label)) return tail;
                return LabelIsAmbiguous ? $"{Label}  ({tail})" : Label;
            }
        }

        /// <summary>
        /// グループ内で出す行名。グループ見出しが文脈を持つので、
        /// ここではグループ根からの相対パスで足りる。
        /// </summary>
        public string RowName
        {
            get
            {
                if (!string.IsNullOrEmpty(Label) && !LabelIsAmbiguous) return Label;

                // Disambiguator は他の行と区別が付く最短の末尾。グループ根からの
                // 相対パスより短いことが多いので、短いほうを採る。
                var inGroup = PathInGroup;
                if (string.IsNullOrEmpty(inGroup)) inGroup = Path;
                if (!string.IsNullOrEmpty(Disambiguator) && Disambiguator.Length < inGroup.Length)
                    inGroup = Disambiguator;

                return string.IsNullOrEmpty(Label) ? inGroup : $"{Label}  ({inGroup})";
            }
        }

        internal static string Tail(string path, int segments)
        {
            var parts = path.Split('/');
            return segments >= parts.Length
                ? path
                : string.Join("/", parts.Skip(parts.Length - segments));
        }
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

            var clipLabels = MenuLabelResolver.BuildClipLabels(descriptor);

            // 複数のトグルが同じクリップを共有していると、クリップからはどのトグルの
            // ラベルなのかを決められない (実測: 左右のペンライトが同一クリップで、
            // 右手にも 'ペンライト出現_L' が付いた)。共有クリップからは採らない。
            var clipUsers = new Dictionary<string, int>();
            foreach (var clips in pathToClips.Values)
                foreach (var clip in clips)
                    clipUsers[clip] = clipUsers.TryGetValue(clip, out var n) ? n + 1 : 1;

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
                    Label = clips
                        .Where(c => clipUsers[c] == 1)
                        .Select(c => clipLabels.TryGetValue(c, out var l) ? l : null)
                        .FirstOrDefault(l => l != null),
                });
            }

            AssignDisplayNames(result);
            AssignGroups(result, root);
            return result;
        }

        /// <summary>
        /// 行を「どこの持ち物か」でまとめる。
        ///
        /// 見出しはプレハブのインスタンス根を最優先にする。ユーザーが認識している
        /// 単位は導入した衣装・ギミックのプレハブ名であって、GameObject の名前ではない。
        /// プレハブでなければ親オブジェクトで代用する。
        /// </summary>
        private static void AssignGroups(List<ToggleCandidate> candidates, Transform root)
        {
            foreach (var candidate in candidates)
            {
                var owner = GroupRootOf(candidate.Object, root);
                candidate.GroupKey = owner == root
                    ? "" : AnimationUtility.CalculateTransformPath(owner, root);
                candidate.GroupLabel = owner == root ? "アバタールート直下" : owner.name;

                candidate.PathInGroup = candidate.Path.Length > candidate.GroupKey.Length
                                        && candidate.Path.StartsWith(candidate.GroupKey)
                    ? candidate.Path.Substring(
                        candidate.GroupKey.Length == 0 ? 0 : candidate.GroupKey.Length + 1)
                    : candidate.Path;
            }
        }

        /// <summary>
        /// 導入単位の祖先を返す。
        ///
        /// 親を 1 段上がるだけでは粒度が細かすぎて、1 件ずつのグループが並ぶうえに
        /// 同名の見出しが重複する(実測: 左右の「ペンライト空中固定」が別グループで並んだ)。
        /// 入れ子プレハブの根が取れるならそれを、無ければアバタールートから 2 段目
        /// (`Wear/SR_14`、`Facial&Posing/AvatarPoseSystem` のような導入単位)を使う。
        /// </summary>
        private static Transform GroupRootOf(GameObject go, Transform root)
        {
            // 祖先をさかのぼって、アバター直下より深いところにある最も外側の
            // プレハブインスタンス根を探す。
            Transform outermostPrefab = null;
            for (var t = go.transform; t != null && t != root; t = t.parent)
                if (PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) == t.gameObject)
                    outermostPrefab = t;

            if (outermostPrefab != null && outermostPrefab != go.transform)
                return outermostPrefab;

            // 祖先チェーンを root 直下から並べ、2 段目を採る。
            var chain = new List<Transform>();
            for (var t = go.transform.parent; t != null && t != root; t = t.parent) chain.Insert(0, t);
            if (chain.Count == 0) return root;
            return chain[Math.Min(1, chain.Count - 1)];
        }

        /// <summary>
        /// 行が互いに区別できるところまでパス末尾を伸ばす。
        /// ラベルが同じ候補どうし (および全ラベル無し) の中で一意になれば足りる。
        /// </summary>
        private static void AssignDisplayNames(List<ToggleCandidate> candidates)
        {
            foreach (var group in candidates.GroupBy(c => c.Label ?? ""))
            {
                var peers = group.ToList();
                var labelled = group.Key.Length > 0;
                if (labelled && peers.Count > 1)
                    foreach (var candidate in peers) candidate.LabelIsAmbiguous = true;

                foreach (var candidate in peers)
                {
                    // ラベルが無い行は親を 1 段添えたほうが読める ('HandleMesh' 単独では意味を成さない)
                    var minimum = labelled ? 1 : 2;
                    var depth = candidate.Path.Split('/').Length;
                    for (var k = minimum; k <= depth; k++)
                    {
                        var tail = ToggleCandidate.Tail(candidate.Path, k);
                        if (peers.Count(p => ToggleCandidate.Tail(p.Path, k) == tail) > 1) continue;
                        candidate.Disambiguator = tail;
                        break;
                    }

                    candidate.Disambiguator ??= candidate.Path;
                }
            }
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
        /// ビルド時にトグルを生成するツールのぶんは、編集時にクリップが無いので出せない。
        /// 実測では Avatar Menu Creator (Narazaka) が該当し、衣装アバターで
        /// 一覧 8 件に対しビルド 18 件になった。MA Object Toggle も同様。
        /// m_IsActive を生成しうる NDMF ツールは無数にあるため、
        /// **編集時の一覧を完全にすることは原理的にできない**。参考表示と割り切る。
        ///
        /// 逆向きの乖離もある。ビルド中に階層を張り替えるパッケージ (APS など) では、
        /// 編集時のパスとビルド時のパスが別物になり、一覧に出たのに変換されない候補が
        /// 生じる (実測: APS_HandleEx)。つまり一覧は変換結果の予告ではない。
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
