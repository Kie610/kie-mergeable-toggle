using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Kie.MergeableToggle.Editor
{
    [CustomEditor(typeof(MergeableToggle))]
    internal sealed class MergeableToggleInspector : UnityEditor.Editor
    {
        private MergeableToggle _component;
        private List<ToggleCandidate> _candidates;
        private bool _needsRescan = true;

        private const float BadgeWidth = 56f;

        private void OnEnable()
        {
            _needsRescan = true;
        }

        public override void OnInspectorGUI()
        {
            _component = (MergeableToggle)target;
            _component.excludedPaths ??= new List<string>();
            _component.forceIncludedPaths ??= new List<string>();
            serializedObject.Update();

            // PropertyField の編集は ApplyModifiedProperties まで対象へ反映されない。
            // 同じ GUI パスで分岐するときは、対象のフィールドではなくプロパティの値を見る
            // (見ないと表示が 1 回遅れる)。
            var enableConversion = serializedObject.FindProperty("enableConversion");
            EditorGUILayout.PropertyField(
                enableConversion, new GUIContent("変換を有効にする"));

            if (!enableConversion.boolValue)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawComponentDisabling();

            if (_needsRescan || _candidates == null)
            {
                var descriptor = _component.GetComponent<VRCAvatarDescriptor>();
                _candidates = descriptor != null
                    ? ToggleScanner.Scan(descriptor, _component.disableComponentsWhenHidden)
                    : new List<ToggleCandidate>();
                _needsRescan = false;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("検出されたメッシュトグル", EditorStyles.boldLabel);

            if (_candidates.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "変換できるトグルが見つかりません。アニメーターで GameObject の\n" +
                    "オンオフをしている SkinnedMeshRenderer が対象です。",
                    MessageType.Info);
            }
            else
            {
                DrawMasterToggle();
                DrawGroups();
                DrawSummary();
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_candidates == null))
            {
                if (GUILayout.Button(
                        new GUIContent("診断情報をコピー",
                            "環境と検出結果をクリップボードへコピーします。不具合の報告に添えてください。\n" +
                            "アバターの階層パスを含みます。共有する前に中身を確認してください。"),
                        EditorStyles.miniButton, GUILayout.Width(110)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildDiagnostics();
                    Debug.Log("[MergeableToggle] 診断情報をクリップボードへコピーしました");
                }
            }

            if (GUILayout.Button("再スキャン", EditorStyles.miniButton, GUILayout.Width(80)))
            {
                _needsRescan = true;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawComponentDisabling()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("disableComponentsWhenHidden"),
                new GUIContent("非表示中はコンポーネントも無効化する"));
            // 警告の判定基準が変わるのでスキャンし直す
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                _needsRescan = true;
            }

            if (!_component.disableComponentsWhenHidden)
            {
                EditorGUILayout.HelpBox(
                    "隠しても中身は動き続けます。PhysBone は揺れ、パーティクルは出て、\n" +
                    "コンタクトは反応します。元のトグルと挙動を揃えるなら有効にしてください。",
                    MessageType.Warning);
            }

            var disablePhysBones = serializedObject.FindProperty("disablePhysBonesWhenHidden");
            EditorGUILayout.PropertyField(
                disablePhysBones,
                new GUIContent("非表示中は専用 PhysBone も止める",
                    "その衣装だけが使っているアーマチュア側の PhysBone を、隠すのと同じ" +
                    "タイミングで無効化します。素体と共有しているボーン (胸・尻尾など) は" +
                    "止めません。何を止めたかはビルドログに出ます。"));

            using (new EditorGUI.DisabledScope(!disablePhysBones.boolValue))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("disableSharedPhysBonesWhenHidden"),
                    new GUIContent("複数トグル共有 PhysBone も止める",
                        "複数の衣装トグルに共有される PhysBone を、所有するトグルが全部" +
                        "非表示のときだけ止めます。FX に専用レイヤーとローカルパラメータを生成します。"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("emptyHiddenMaterialSlots"),
                new GUIContent("非表示中は専用マテリアルスロットを空にする",
                    "隠れた衣装だけが使うマテリアルスロットを、隠れている間だけ空のシェーダへ" +
                    "差し替えて描画コストを消します。素体と共有するスロットは触りません。" +
                    "Android ビルドでは無効です。何を差し替えたかはビルドログに出ます。"));
        }

        private bool IsIncluded(ToggleCandidate candidate)
        {
            return candidate.IsClean
                ? !_component.excludedPaths.Contains(candidate.Path)
                : _component.forceIncludedPaths.Contains(candidate.Path);
        }

        private void SetIncluded(ToggleCandidate candidate, bool included)
        {
            Undo.RecordObject(_component, "Toggle Conversion Target");

            // 候補の安全性 (IsClean) は、警告の元になっているコンポーネントを足したり
            // 外したりすると入れ替わる。片方のリストだけを触ると反対側へ古い選択が残り、
            // 安全性が戻ったときにその古い選択が復活する。両方から消してから入れ直す。
            // 既存データに重複パスがあっても、ここで一緒に落ちる。
            _component.excludedPaths.RemoveAll(path => path == candidate.Path);
            _component.forceIncludedPaths.RemoveAll(path => path == candidate.Path);
            if (candidate.IsClean)
            {
                if (!included) _component.excludedPaths.Add(candidate.Path);
            }
            else
            {
                if (included) _component.forceIncludedPaths.Add(candidate.Path);
            }

            EditorUtility.SetDirty(_component);
        }

        private void DrawMasterToggle()
        {
            var includedCount = _candidates.Count(IsIncluded);
            var allIncluded = includedCount == _candidates.Count;

            EditorGUILayout.BeginHorizontal();
            EditorGUI.showMixedValue = includedCount > 0 && !allIncluded;
            var newMaster = EditorGUILayout.ToggleLeft(
                $"すべて選択 ({includedCount}/{_candidates.Count})", allIncluded);
            EditorGUI.showMixedValue = false;
            EditorGUILayout.EndHorizontal();

            if (newMaster != allIncluded)
            {
                foreach (var candidate in _candidates) SetIncluded(candidate, newMaster);
            }
        }

        private readonly Dictionary<string, bool> _groupFoldouts = new Dictionary<string, bool>();

        /// <summary>
        /// 持ち主(プレハブ／親オブジェクト)ごとに畳んで出す。
        /// 平らな一覧だと 'HandleMesh' のような名前が文脈なしで並んで判別できない。
        /// </summary>
        private void DrawGroups()
        {
            foreach (var group in _candidates.GroupBy(c => c.GroupKey).OrderBy(g => g.Key))
            {
                var members = group.ToList();
                var key = group.Key ?? "";
                var label = members[0].GroupLabel;

                if (!_groupFoldouts.ContainsKey(key)) _groupFoldouts[key] = true;

                var includedCount = members.Count(IsIncluded);
                var allIncluded = includedCount == members.Count;

                EditorGUILayout.BeginHorizontal();
                _groupFoldouts[key] = EditorGUILayout.Foldout(
                    _groupFoldouts[key],
                    new GUIContent($"{label} ({members.Count}件)", key),
                    true);

                EditorGUI.showMixedValue = includedCount > 0 && !allIncluded;
                var rect = GUILayoutUtility.GetRect(14, EditorGUIUtility.singleLineHeight, GUILayout.Width(14));
                var newMaster = EditorGUI.Toggle(rect, allIncluded);
                EditorGUI.showMixedValue = false;
                EditorGUILayout.EndHorizontal();

                if (newMaster != allIncluded)
                    foreach (var candidate in members) SetIncluded(candidate, newMaster);

                if (!_groupFoldouts[key]) continue;
                foreach (var candidate in members) DrawCandidateRow(candidate);
            }
        }

        private void DrawCandidateRow(ToggleCandidate candidate)
        {
            // 一覧はスキャン時点のもの。再スキャンせずに対象を消すと参照が切れるので、
            // 描画側で落としておく (Missing になった行は次の再スキャンで消える)。
            if (candidate.Object == null) return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);

            var included = IsIncluded(candidate);
            var toggleRect = GUILayoutUtility.GetRect(14, EditorGUIUtility.singleLineHeight, GUILayout.Width(14));
            var newIncluded = EditorGUI.Toggle(toggleRect, included);
            if (newIncluded != included) SetIncluded(candidate, newIncluded);

            var label = new GUIContent(candidate.RowName,
                $"オブジェクト: {candidate.Object.name}\n{candidate.Path}\n" +
                $"トグル元クリップ: {string.Join(", ", candidate.SourceClips)}");
            EditorGUILayout.LabelField(label);

            EditorGUILayout.LabelField($"SMR {candidate.Renderers.Count}", GUILayout.Width(50));

            EditorGUILayout.LabelField(
                new GUIContent($"{VertexCount(candidate):N0} 頂点",
                    "このトグルで隠れる頂点数。変換すると、隠している間もこのぶんを\n" +
                    "毎フレーム払い続けます (統合したメッシュは常に表示されるため)。\n" +
                    "収支はトグルの数ではなく、この頂点数で決まります。"),
                EditorStyles.miniLabel, GUILayout.Width(76));

            DrawRowBadge(candidate, included);

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 候補が持つ頂点数の合計。収支を決めるのはトグルの数ではなく隠した頂点数なので、
        /// 利用者が除外を判断する材料はこれになる (2026-08-30 の実測。
        /// Docs/hidden-cost-decision.md と Docs/perf-research-backlog.md 目標 E)。
        /// </summary>
        private static int VertexCount(ToggleCandidate candidate)
        {
            return candidate.Renderers.Sum(
                r => r != null && r.sharedMesh != null ? r.sharedMesh.vertexCount : 0);
        }

        /// <summary>
        /// 行末のバッジ。適用不可を最優先で出す。変換できない候補はビルド時に
        /// 黙って対象から外れるので、そこだけは必ず見えるようにする。
        /// </summary>
        private void DrawRowBadge(ToggleCandidate candidate, bool included)
        {
            var style = new GUIStyle(EditorStyles.miniLabel);

            if (included)
            {
                var blocked = candidate.Renderers
                    .Where(r => !InfinimationHider.CanApply(r))
                    .ToList();

                if (blocked.Count > 0)
                {
                    style.normal.textColor = new Color(1f, 0.35f, 0.3f);
                    EditorGUILayout.LabelField(
                        new GUIContent("適用不可",
                            "隠蔽シェイプを追加できないメッシュがあります:\n" +
                            string.Join(", ", blocked.Select(r => r.name)) + "\n\n" +
                            "頂点を持つ SkinnedMeshRenderer が対象です。"),
                        style, GUILayout.Width(BadgeWidth));
                    return;
                }
            }

            if (!candidate.IsClean)
            {
                style.normal.textColor = new Color(1f, 0.6f, 0f);
                EditorGUILayout.LabelField(
                    new GUIContent("要注意",
                        "非表示中も止められないコンポーネントが含まれます:\n" +
                        string.Join(", ", candidate.Warnings)),
                    style, GUILayout.Width(BadgeWidth));
                return;
            }

            GUILayout.Space(BadgeWidth + 4);
        }

        /// <summary>
        /// 不具合報告へ貼るための環境と検出結果。alpha の間、報告のたびに口頭で
        /// 聞いている項目をここでまとめて出す。**性能の主張はしない** (分岐点などの
        /// 数値は構図で動くので、報告用の文書へ載せない)。
        /// </summary>
        private string BuildDiagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"kieMergeableToggle 診断情報  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            sb.AppendLine();
            sb.AppendLine("[環境]");
            sb.AppendLine($"Unity\t{Application.unityVersion}");
            foreach (var id in new[]
                     {
                         "com.kie.kie-mergeable-toggle", "com.vrchat.avatars", "nadena.dev.ndmf",
                         "com.anatawa12.avatar-optimizer", "nadena.dev.modular-avatar",
                     })
                sb.AppendLine($"{id}\t{PackageVersion(id)}");
            sb.AppendLine($"build target\t{EditorUserBuildSettings.activeBuildTarget}");

            sb.AppendLine();
            sb.AppendLine("[対象]");
            var descriptor = _component != null ? _component.GetComponent<VRCAvatarDescriptor>() : null;
            sb.AppendLine($"アバター\t{(_component != null ? _component.gameObject.name : "(不明)")}");
            sb.AppendLine($"VRCAvatarDescriptor\t{(descriptor != null ? "あり" : "なし")}");

            sb.AppendLine();
            sb.AppendLine("[設定]");
            if (_component != null)
            {
                sb.AppendLine($"enableConversion\t{_component.enableConversion}");
                sb.AppendLine($"disableComponentsWhenHidden\t{_component.disableComponentsWhenHidden}");
                sb.AppendLine($"disablePhysBonesWhenHidden\t{_component.disablePhysBonesWhenHidden}");
                sb.AppendLine($"disableSharedPhysBonesWhenHidden\t{_component.disableSharedPhysBonesWhenHidden}");
                sb.AppendLine($"excludedPaths\t{_component.excludedPaths?.Count ?? 0} 件");
                sb.AppendLine($"forceIncludedPaths\t{_component.forceIncludedPaths?.Count ?? 0} 件");
            }

            sb.AppendLine();
            sb.AppendLine("[候補] 対象/クリーン/SMR/頂点/パス/警告");
            var candidates = _candidates ?? new List<ToggleCandidate>();
            var missing = 0;
            foreach (var candidate in candidates)
            {
                if (candidate.Object == null) { missing++; continue; }
                var vertices = candidate.Renderers.Sum(
                    r => r != null && r.sharedMesh != null ? r.sharedMesh.vertexCount : 0);
                sb.AppendLine(string.Join("\t",
                    IsIncluded(candidate) ? "o" : "-",
                    candidate.IsClean ? "clean" : "warn",
                    candidate.Renderers.Count.ToString(),
                    vertices.ToString(),
                    candidate.Path,
                    string.Join("; ", candidate.Warnings)));
            }
            if (candidates.Count == 0) sb.AppendLine("(候補なし)");

            var included = candidates.Where(c => c.Object != null).Where(IsIncluded).ToList();
            var includedRenderers = included.SelectMany(c => c.Renderers).Distinct().ToList();
            sb.AppendLine();
            sb.AppendLine("[合計]");
            sb.AppendLine($"変換対象のトグル\t{included.Count} / {candidates.Count - missing}");
            sb.AppendLine($"SkinnedMeshRenderer\t{includedRenderers.Count}");
            sb.AppendLine($"頂点\t{includedRenderers.Sum(r => r != null && r.sharedMesh != null ? r.sharedMesh.vertexCount : 0)}");
            if (missing > 0)
                sb.AppendLine($"(参照が切れた候補 {missing} 件は省略。再スキャンしてください)");

            sb.AppendLine();
            sb.AppendLine("不具合の報告には、これに加えて「素のアバター / 本ツール単独 / AAO 単独」の");
            sb.AppendLine("切り分け結果と、ビルド時のコンソールログを添えてください。");
            return sb.ToString();
        }

        private static string PackageVersion(string id)
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{id}/package.json");
                return info != null ? info.version : "(未導入)";
            }
            catch (Exception e)
            {
                return "(取得できず: " + e.GetType().Name + ")";
            }
        }

        private void DrawSummary()
        {
            var included = _candidates.Where(IsIncluded).ToList();
            var renderers = included.SelectMany(c => c.Renderers).Distinct().ToList();
            var rendererCount = renderers.Count;
            var vertexCount = renderers.Sum(
                r => r != null && r.sharedMesh != null ? r.sharedMesh.vertexCount : 0);

            var lines = new List<string>
            {
                $"変換対象: トグル {included.Count} 件 / SkinnedMeshRenderer {rendererCount} 個 " +
                $"/ 合計 {vertexCount:N0} 頂点",
            };

            if (vertexCount > 0)
            {
                lines.Add(
                    "変換すると、隠している間もこの頂点を毎フレーム払い続けます。そのかわり\n" +
                    "表示中はレンダラーとマテリアルスロットが減ります。実測では、この合計のうち\n" +
                    "普段隠している割合が 9 割を超えたあたりで損得が釣り合います。つまり\n" +
                    "ほとんど着ないトグルが 1 つだけ極端に大きい、という場合を除けば\n" +
                    "全部変換して得です。判断に迷ったら、上の一覧で頂点数の大きいものから\n" +
                    "外してください。");
            }

            lines.Add("この一覧はビルド結果の予告ではありません。\n" +
                      "ビルド時にトグルを生成するツール(Avatar Menu Creator、MA Object Toggle など)の\n" +
                      "ぶんは、まだクリップが存在しないのでここに出ません。逆に、ビルド中に階層を\n" +
                      "組み替えるツール(AvatarPoseSystem など)のぶんは、出ていても変換されません。\n" +
                      "実際に変換されたものはビルドログに出力されます。");

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(string.Join("\n", lines), MessageType.None);
        }

    }
}
