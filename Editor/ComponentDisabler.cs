using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace Kie.MergeableToggle.Editor
{
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
        public static void AddDisableBindings(ToggleCandidate target, Transform root, HidePlan plan)
        {
            var converted = new HashSet<SkinnedMeshRenderer>(target.Renderers);
            var seen = new HashSet<(string path, System.Type type)>();

            foreach (var component in target.Object.GetComponentsInChildren<Component>(true))
            {
                if (!CanDisable(component)) continue;
                if (component is SkinnedMeshRenderer renderer && converted.Contains(renderer)) continue;

                var path = AnimationUtility.CalculateTransformPath(component.transform, root);
                // 同じオブジェクトに同じ型が複数あってもバインディングは1本しか作れない
                // (Unity 側の制約)。重複を落として1本にまとめる。
                if (!seen.Add((path, component.GetType()))) continue;

                plan.Toggled.Add((
                    EditorCurveBinding.FloatCurve(path, component.GetType(), "m_Enabled"), 1f, 0f));
            }
        }

        /// <summary>m_Enabled を持ち、かつ落として意味のあるコンポーネントか。</summary>
        public static bool CanDisable(Component component)
        {
            if (component == null) return false;
            // IEditorOnly はビルド中に消えるので触っても無意味
            if (component is Transform || component is IEditorOnly) return false;

            using var serialized = new SerializedObject(component);
            return serialized.FindProperty("m_Enabled") != null;
        }
    }
}
