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

            if (_needsRescan || _candidates == null)
            {
                var descriptor = _component.GetComponent<VRCAvatarDescriptor>();
                _candidates = descriptor != null ? ToggleScanner.Scan(descriptor) : new List<ToggleCandidate>();
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

            if (!candidate.IsClean)
            {
                var style = new GUIStyle(EditorStyles.miniLabel);
                style.normal.textColor = new Color(1f, 0.6f, 0f);
                EditorGUILayout.LabelField(
                    new GUIContent("要注意", "メッシュ以外のコンポーネントが常時アクティブになります:\n" +
                                            string.Join(", ", candidate.Warnings)),
                    style, GUILayout.Width(50));
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            var included = _candidates.Where(IsIncluded).ToList();
            var rendererCount = included.SelectMany(c => c.Renderers).Distinct().Count();

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                $"変換対象: トグル {included.Count} 件 / SkinnedMeshRenderer {rendererCount} 個\n" +
                "トグル挙動を保ったまま、ビルド時に NaNimation (ボーンスケール切替) へ変換され、\n" +
                "Avatar Optimizer がメッシュを統合できるようになります。ボーン数は少し増えます。",
                MessageType.None);
        }
    }
}
