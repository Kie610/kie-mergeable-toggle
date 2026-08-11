using System;
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
        private bool _showAdvanced;

        /// <summary>行のポップアップは先頭が「既定」。既定と同じ機構を選んでも上書きは持たない。</summary>
        private static readonly HideMethod[] MethodOrder =
        {
            HideMethod.BlendShape, HideMethod.BlendShapeAxis,
            HideMethod.UVTileDiscard, HideMethod.NaNimation,
        };

        private static readonly string[] MethodLabels =
        {
            "シェイプ(関節)", "シェイプ(軸)", "UVタイル破棄", "NaNimation",
        };

        private static readonly string[] RowMethodOptions =
            new[] { "既定" }.Concat(MethodLabels).ToArray();

        private const float BadgeWidth = 56f;

        private static string LabelOf(HideMethod method)
        {
            var i = Array.IndexOf(MethodOrder, method);
            return i < 0 ? method.ToString() : MethodLabels[i];
        }

        private void OnEnable()
        {
            _needsRescan = true;
        }

        public override void OnInspectorGUI()
        {
            _component = (MergeableToggle)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("enableConversion"),
                new GUIContent("変換を有効にする"));

            if (!_component.enableConversion)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawDefaultMethod();
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
                foreach (var candidate in _candidates) DrawCandidateRow(candidate);
                DrawSummary();
            }

            DrawAdvanced();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_candidates.Count == 0))
            {
                if (GUILayout.Button(
                        new GUIContent("自動で割り当てる",
                            "畳んだあとに実際どれだけ面が見えるかを、静止時と着座ポーズで測って\n" +
                            "トグルごとの機構を決めます。ポーズは一時的に付けて元へ戻します。"),
                        EditorStyles.miniButton, GUILayout.Width(110)))
                {
                    AutoAssign();
                }
            }

            if (GUILayout.Button("再スキャン", EditorStyles.miniButton, GUILayout.Width(80)))
            {
                _needsRescan = true;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            if (_lastAdvice != null)
            {
                EditorGUILayout.HelpBox(_lastAdvice, MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 既定の隠しかた。enum の PropertyField をそのまま出すと英語の識別子が見えて
        /// 行のポップアップと表記が揃わないので、同じラベルで描く。
        /// </summary>
        private void DrawDefaultMethod()
        {
            var property = serializedObject.FindProperty("hideMethod");
            var index = Array.IndexOf(MethodOrder, (HideMethod)property.enumValueIndex);
            if (index < 0) index = 0;

            EditorGUI.BeginChangeCheck();
            index = EditorGUILayout.Popup(new GUIContent("既定の隠しかた"), index, MethodLabels);
            if (EditorGUI.EndChangeCheck())
                property.enumValueIndex = (int)MethodOrder[index];

            EditorGUILayout.HelpBox(DescribeMethod(MethodOrder[index]), MessageType.None);
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
        }

        private string _lastAdvice;

        /// <summary>
        /// 実測にもとづいてトグルごとの機構を決め、上書きとして書き込む。
        /// 既定と同じ結論になったトグルは上書きを持たない(MethodFor が既定へ落ちる)。
        /// </summary>
        private void AutoAssign()
        {
            var included = _candidates.Where(IsIncluded).ToList();
            if (included.Count == 0) return;

            List<MethodAdvisor.Advice> advices;
            try
            {
                EditorUtility.DisplayProgressBar("Mergeable Toggle", "畳んだあとの見えかたを測っています", 0.5f);
                advices = MethodAdvisor.Analyze(_component.transform, included);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Undo.RecordObject(_component, "Auto Assign Hide Methods");
            foreach (var advice in advices)
            {
                _component.SetMethodFor(
                    advice.Candidate.Path,
                    advice.Method == _component.hideMethod ? (HideMethod?)null : advice.Method);
            }

            EditorUtility.SetDirty(_component);

            _lastAdvice = "自動割り当ての結果:\n" + string.Join("\n", advices
                .OrderBy(a => a.Candidate.Path)
                .Select(a => $"{a.Candidate.Object.name} → {LabelOf(a.Method)} ({a.Reason})"));
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

        private void DrawCandidateRow(ToggleCandidate candidate)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);

            var included = IsIncluded(candidate);
            var toggleRect = GUILayoutUtility.GetRect(14, EditorGUIUtility.singleLineHeight, GUILayout.Width(14));
            var newIncluded = EditorGUI.Toggle(toggleRect, included);
            if (newIncluded != included) SetIncluded(candidate, newIncluded);

            var label = new GUIContent(candidate.Object.name,
                $"{candidate.Path}\nトグル元クリップ: {string.Join(", ", candidate.SourceClips)}");
            EditorGUILayout.LabelField(label);

            EditorGUILayout.LabelField($"SMR {candidate.Renderers.Count}", GUILayout.Width(50));

            using (new EditorGUI.DisabledScope(!included)) DrawMethodPopup(candidate);

            DrawRowBadge(candidate, included);

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 行末のバッジ。適用不可を最優先で出す。選んだ機構が使えない候補はビルド時に
        /// 黙って変換対象から外れるので、そこだけは必ず見えるようにする。
        /// </summary>
        private void DrawRowBadge(ToggleCandidate candidate, bool included)
        {
            var style = new GUIStyle(EditorStyles.miniLabel);

            if (included)
            {
                var method = _component.MethodFor(candidate.Path);
                var blocked = candidate.Renderers
                    .Where(r => !ToggleConverter.CanApply(method, r))
                    .ToList();

                if (blocked.Count > 0)
                {
                    style.normal.textColor = new Color(1f, 0.35f, 0.3f);
                    EditorGUILayout.LabelField(
                        new GUIContent("適用不可",
                            $"{LabelOf(method)} を適用できないメッシュがあります:\n" +
                            string.Join(", ", blocked.Select(r => r.name)) + "\n\n" +
                            UnavailableHint(method)),
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

        private static string UnavailableHint(HideMethod method)
        {
            switch (method)
            {
                case HideMethod.UVTileDiscard:
                    return "UVタイル破棄は lilToon 系シェーダ専用で、メッシュが UV3 を\n" +
                           "使っていないことが条件です。";
                default:
                    return "スキンウェイトを持つメッシュが対象です。";
            }
        }

        private void DrawMethodPopup(ToggleCandidate candidate)
        {
            var current = _component.methodOverrides
                .FirstOrDefault(o => o != null && o.path == candidate.Path);
            var index = current == null ? 0 : Array.IndexOf(MethodOrder, current.method) + 1;

            var newIndex = EditorGUILayout.Popup(index, RowMethodOptions, GUILayout.Width(110));
            if (newIndex == index) return;

            Undo.RecordObject(_component, "Change Hide Method");
            _component.SetMethodFor(
                candidate.Path, newIndex <= 0 ? (HideMethod?)null : MethodOrder[newIndex - 1]);
            EditorUtility.SetDirty(_component);
        }

        private void DrawSummary()
        {
            var included = _candidates.Where(IsIncluded).ToList();
            var rendererCount = included.SelectMany(c => c.Renderers).Distinct().Count();

            var byMethod = MethodOrder
                .Select(m => (method: m, count: included.Count(c => _component.MethodFor(c.Path) == m)))
                .Where(x => x.count > 0)
                .Select(x => $"{LabelOf(x.method)} {x.count}");

            var lines = new List<string>
            {
                $"変換対象: トグル {included.Count} 件 / SkinnedMeshRenderer {rendererCount} 個",
                "内訳: " + string.Join(" / ", byMethod),
            };

            var nan = included.Count(c => _component.MethodFor(c.Path) == HideMethod.NaNimation);
            if (nan > 0) lines.Add($"NaNimation {nan} 件のぶんだけボーン数が増えます。");

            var tiles = included.Count(c => _component.MethodFor(c.Path) == HideMethod.UVTileDiscard);
            if (tiles > UVTileDiscardHider.UsableTileCount)
            {
                lines.Add($"UVタイル破棄が上限 {UVTileDiscardHider.UsableTileCount} 件を超えています" +
                          $"({tiles} 件)。超過分は変換されません。");
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(string.Join("\n", lines), MessageType.None);
        }

        private void DrawAdvanced()
        {
            EditorGUILayout.Space();
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "詳細設定", true);
            if (!_showAdvanced) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("skipInitiallyHiddenMaterialClone"),
                    new GUIContent(
                        "初期非表示のマテリアルを複製しない",
                        "UV タイル破棄でのみ効きます。"));

                if (_component.skipInitiallyHiddenMaterialClone)
                {
                    EditorGUILayout.HelpBox(
                        "マテリアルスロットを1つ余分に詰められる代わりに、ワールド入室時や\n" +
                        "アバター切替時に、初期非表示のメッシュが一瞬見えます。\n" +
                        "見える長さはアニメーターの初期化タイミング次第で、\n" +
                        "1フレームとは限りません。実機で確認してください。",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "UV タイル破棄のセットアップ値はアニメーターの初回適用まで効かないため、\n" +
                        "初期非表示のトグルはマテリアルを複製してシリアライズ値で隠しています。\n" +
                        "複製したマテリアルは Avatar Optimizer がスロット統合できません。",
                        MessageType.None);
                }
            }
        }

        private static string DescribeMethod(HideMethod method)
        {
            switch (method)
            {
                case HideMethod.UVTileDiscard:
                    return "lilToon の UV タイル破棄で消します。頂点シェーダで座標を NaN に\n" +
                           "するので完全に消え、ボーンもコンストレイントも増えません。\n" +
                           "畳んだ面が見えてしまう上着やスカートに向きます。\n" +
                           "lilToon 系シェーダ専用・PC 限定・使えるタイルは 15 個までです。";
                case HideMethod.BlendShapeAxis:
                    return "頂点をボーンの軸線へ吸着させるブレンドシェイプを生成します。\n" +
                           "畳まれた面が手足の内部に沿って残り、手足と一緒に動くため、\n" +
                           "関節を曲げたときに体表から飛び出しにくくなります。\n" +
                           "タイツ・スラックスなど肌に密着して関節を跨ぐ衣装向け。\n" +
                           "逆にスカートやコートでは静止時の残りが増えるので不向きです。";
                case HideMethod.NaNimation:
                    return "ボーンを複製してスケールを NaN に切り替えます。完全に消えますが、\n" +
                           "boneCount が増えます。パフォーマンスランクの計上項目なので、\n" +
                           "PC では UV タイル破棄を優先してください。\n" +
                           "Mobile ビルドや非 lilToon マテリアルでの退避先です。";
                default:
                    return "頂点を骨格上へ畳むブレンドシェイプを生成し、0⇔100 で切り替えます。\n" +
                           "ランクの計上項目を一切増やさず、Mobile でも使えます。\n" +
                           "隠した形状は消滅せず骨格上へ畳まれるため、体で覆われない部位では\n" +
                           "畳まれた面が見えることがあります。実際の見えかたを確認してください。";
            }
        }
    }
}
