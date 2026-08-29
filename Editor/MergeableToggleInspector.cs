using System.Collections.Generic;
using System.Linq;
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

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("enableConversion"),
                new GUIContent("変換を有効にする"));

            if (!_component.enableConversion)
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

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("disablePhysBonesWhenHidden"),
                new GUIContent("非表示中は専用 PhysBone も止める",
                    "その衣装だけが使っているアーマチュア側の PhysBone を、隠すのと同じ" +
                    "タイミングで無効化します。素体と共有しているボーン (胸・尻尾など) は" +
                    "止めません。何を止めたかはビルドログに出ます。"));

            using (new EditorGUI.DisabledScope(!_component.disablePhysBonesWhenHidden))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("disableSharedPhysBonesWhenHidden"),
                    new GUIContent("複数トグル共有 PhysBone も止める",
                        "複数の衣装トグルに共有される PhysBone を、所有するトグルが全部" +
                        "非表示のときだけ止めます。FX に専用レイヤーとローカルパラメータを生成します。"));
                EditorGUI.indentLevel--;
            }
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
            if (candidate.IsClean)
            {
                if (included) _component.excludedPaths.Remove(candidate.Path);
                else if (!_component.excludedPaths.Contains(candidate.Path))
                    _component.excludedPaths.Add(candidate.Path);
            }
            else
            {
                if (!included) _component.forceIncludedPaths.Remove(candidate.Path);
                else if (!_component.forceIncludedPaths.Contains(candidate.Path))
                    _component.forceIncludedPaths.Add(candidate.Path);
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
