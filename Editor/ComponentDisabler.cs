using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace Kie.MergeableToggle.Editor
{
    internal enum ComponentDisableStatus
    {
        Disable,
        AlreadyDisabled,
        DuplicateType,
        AlreadyAnimated,
        MissingEnabledProperty,
    }

    /// <summary>
    /// トグル対象を常時アクティブ化する副作用を打ち消す。
    ///
    /// 隠蔽機構はメッシュを見えなくするだけなので、対象を常時アクティブにすると
    /// 中の PhysBone は揺れ続け、パーティクルは出続け、コンタクトは反応し続ける。
    /// 元の m_IsActive と同じタイミングで各コンポーネントの m_Enabled を落として揃える。
    ///
    /// 変換対象の SkinnedMeshRenderer だけは対象外。そこを無効化すると隠蔽機構ごと
    /// 止まってしまう(それに m_Enabled を animate すると AAO の統合条件から外れる)。
    /// </summary>
    internal static class ComponentDisabler
    {
        public static HashSet<Renderer> AddDisableBindings(ToggleCandidate target, Transform root,
            HidePlan plan, Func<EditorCurveBinding, bool> isEnabledAlreadyAnimated,
            bool initiallyHidden)
        {
            var converted = new HashSet<SkinnedMeshRenderer>(target.Renderers);
            var candidates = target.Object.GetComponentsInChildren<Component>(true)
                .Where(component => component != null)
                .Where(component => component is not Transform && component is not IEditorOnly)
                .Where(component => component is not SkinnedMeshRenderer renderer || !converted.Contains(renderer))
                .Select(component => (
                    component,
                    path: AnimationUtility.CalculateTransformPath(component.transform, root),
                    type: component.GetType()))
                .GroupBy(item => (item.path, item.type));
            var hiddenRenderers = new HashSet<Renderer>();
            var skipped = new List<string>();

            foreach (var group in candidates)
            {
                var items = group.ToList();
                var label = $"'{group.Key.path}' ({group.Key.type.Name})";
                var duplicateReported = false;
                foreach (var item in items)
                {
                    var status = Analyze(
                        item.component, item.path, items.Count, isEnabledAlreadyAnimated, out var binding);
                    switch (status)
                    {
                        case ComponentDisableStatus.AlreadyDisabled:
                            skipped.Add($"{label}: m_Enabled is false at build time");
                            continue;
                        case ComponentDisableStatus.DuplicateType:
                            if (!duplicateReported)
                            {
                                skipped.Add($"{label}: same type appears {items.Count} times on one GameObject");
                                duplicateReported = true;
                            }
                            continue;
                        case ComponentDisableStatus.AlreadyAnimated:
                            skipped.Add($"{label}: m_Enabled is already animated");
                            continue;
                        case ComponentDisableStatus.MissingEnabledProperty:
                            continue;
                    }

                    plan.Toggled.Add((binding, 1f, 0f));
                    if (item.component is Renderer renderer) hiddenRenderers.Add(renderer);

                    if (initiallyHidden)
                    {
                        using var serialized = new SerializedObject(item.component);
                        var enabled = serialized.FindProperty("m_Enabled");
                        enabled.boolValue = false;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }

            if (skipped.Count > 0)
                Debug.LogWarning($"[MergeableToggle] skipped component disabling under '{target.Path}': " +
                                 string.Join("; ", skipped));

            return hiddenRenderers;
        }

        /// <summary>
        /// コンポーネントを実際に無効化できるかを判定する。
        /// 編集時のクリーン判定とビルド時のバインディング生成は必ずこの結果を共有する。
        /// </summary>
        public static ComponentDisableStatus Analyze(
            Component component, string path, int sameTypeCount,
            Func<EditorCurveBinding, bool> isEnabledAlreadyAnimated,
            out EditorCurveBinding binding)
        {
            binding = default;
            using var serialized = new SerializedObject(component);
            var enabled = serialized.FindProperty("m_Enabled");
            if (enabled == null)
                return ComponentDisableStatus.MissingEnabledProperty;

            if (!enabled.boolValue)
                return ComponentDisableStatus.AlreadyDisabled;

            if (sameTypeCount > 1)
                return ComponentDisableStatus.DuplicateType;

            binding = EditorCurveBinding.FloatCurve(path, component.GetType(), "m_Enabled");
            if (isEnabledAlreadyAnimated(binding))
                return ComponentDisableStatus.AlreadyAnimated;

            return ComponentDisableStatus.Disable;
        }
    }
}
