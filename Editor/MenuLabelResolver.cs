using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>
    /// トグル候補に、ユーザーがメニューで見ているラベル(「ケープ」等)を割り当てる。
    ///
    /// GameObject 名だけでは一覧が判別できない。実アバターでは 'HandleMesh' が 5 個
    /// 並んだり、'back' 'back2' 'ring' のように何のことか分からない名前が出る。
    ///
    /// 辿りかたは クリップ → そのクリップを含むレイヤーが見ているパラメータ →
    /// 同じパラメータを駆動するメニューコントロールの名前。
    /// メニューから駆動されていないトグル (APS のハンドル等) にはラベルが無いので、
    /// 呼び出し側でパスへフォールバックする。
    /// </summary>
    internal static class MenuLabelResolver
    {
        /// <summary>クリップ名 → メニューラベル。候補が持つ SourceClips で引く。</summary>
        public static Dictionary<string, string> BuildClipLabels(VRCAvatarDescriptor descriptor)
        {
            var parameterLabels = CollectMenuLabels(descriptor);
            var result = new Dictionary<string, string>();
            if (parameterLabels.Count == 0) return result;

            // クリップ名 -> そのクリップへ辿り着く遷移条件のパラメータ。
            // レイヤー単位でまとめると、L/R を1レイヤーで捌いている構成で
            // 取り違える (実測: ペンライトの左右が両方 _L になった)。
            var clipParameters = new Dictionary<string, HashSet<string>>();

            foreach (var controller in EnumerateControllers(descriptor))
                foreach (var layer in controller.layers)
                    CollectStates(layer?.stateMachine, clipParameters);

            foreach (var (clip, parameters) in clipParameters)
            {
                var labels = parameters
                    .Where(parameterLabels.ContainsKey)
                    .Select(p => parameterLabels[p])
                    .Distinct()
                    .ToList();

                // 候補が割れたら黙って片方を採らない。誤ったラベルは無いより悪い。
                if (labels.Count == 1) result[clip] = labels[0];
            }

            return result;
        }

        /// <summary>パラメータ名 → 表示ラベル。先に見つかったほうを優先する。</summary>
        private static Dictionary<string, string> CollectMenuLabels(VRCAvatarDescriptor descriptor)
        {
            var labels = new Dictionary<string, string>();

            void Add(string parameter, string label)
            {
                if (string.IsNullOrEmpty(parameter) || string.IsNullOrEmpty(label)) return;
                if (!labels.ContainsKey(parameter)) labels[parameter] = label;
            }

            void Walk(VRCExpressionsMenu menu, HashSet<VRCExpressionsMenu> seen)
            {
                if (menu == null || !seen.Add(menu)) return; // 循環したサブメニュー対策
                foreach (var control in menu.controls)
                {
                    if (control == null) continue;
                    Add(control.parameter?.name, control.name);
                    Walk(control.subMenu, seen);
                }
            }

            Walk(descriptor.expressionsMenu, new HashSet<VRCExpressionsMenu>());

#if MT_MA_PRESENT
            // MA のメニュー項目はビルドまで VRCExpressionsMenu へ入らないので、
            // コンポーネントからも拾う。ラベルが空なら GameObject 名がそのまま出る。
            foreach (var item in descriptor
                         .GetComponentsInChildren<nadena.dev.modular_avatar.core.ModularAvatarMenuItem>(true))
            {
                if (item == null || item.Control == null) continue;
                Add(item.Control.parameter?.name,
                    string.IsNullOrEmpty(item.Control.name) ? item.gameObject.name : item.Control.name);
            }
#endif

            return labels;
        }

        /// <summary>
        /// ステート単位で「そのステートへ入る遷移が見ているパラメータ」を集め、
        /// そのステートが再生するクリップへ結びつける。
        /// </summary>
        private static void CollectStates(
            AnimatorStateMachine root, Dictionary<string, HashSet<string>> clipParameters)
        {
            if (root == null) return;

            var machines = new List<AnimatorStateMachine>();
            var seen = new HashSet<AnimatorStateMachine>();
            void Descend(AnimatorStateMachine m)
            {
                if (m == null || !seen.Add(m)) return;
                machines.Add(m);
                foreach (var sub in m.stateMachines) Descend(sub.stateMachine);
            }
            Descend(root);

            // 遷移先ステート -> 条件パラメータ
            var incoming = new Dictionary<AnimatorState, HashSet<string>>();
            void Add(AnimatorState destination, IEnumerable<AnimatorCondition> conditions)
            {
                if (destination == null || conditions == null) return;
                if (!incoming.TryGetValue(destination, out var set))
                    incoming[destination] = set = new HashSet<string>();
                foreach (var condition in conditions) set.Add(condition.parameter);
            }

            foreach (var machine in machines)
            {
                foreach (var transition in machine.anyStateTransitions)
                    Add(transition?.destinationState, transition?.conditions);
                foreach (var transition in machine.entryTransitions)
                    Add(transition?.destinationState, transition?.conditions);
                foreach (var child in machine.states)
                    foreach (var transition in child.state?.transitions ?? System.Array.Empty<AnimatorStateTransition>())
                        Add(transition?.destinationState, transition?.conditions);
            }

            foreach (var machine in machines)
            foreach (var child in machine.states)
            {
                var state = child.state;
                if (state == null) continue;

                var parameters = incoming.TryGetValue(state, out var set)
                    ? new HashSet<string>(set)
                    : new HashSet<string>();

                var clips = new HashSet<string>();
                CollectMotion(state.motion, parameters, clips);

                foreach (var clip in clips)
                {
                    if (!clipParameters.TryGetValue(clip, out var existing))
                        clipParameters[clip] = existing = new HashSet<string>();
                    existing.UnionWith(parameters);
                }
            }
        }

        private static void CollectMotion(Motion motion, HashSet<string> parameters, HashSet<string> clips)
        {
            switch (motion)
            {
                case AnimationClip clip:
                    clips.Add(clip.name);
                    return;
                case BlendTree tree:
                    parameters.Add(tree.blendParameter);
                    parameters.Add(tree.blendParameterY);
                    foreach (var child in tree.children) CollectMotion(child.motion, parameters, clips);
                    return;
            }
        }

        /// <summary>
        /// ToggleScanner.EnumerateClips と同じ経路をコントローラ単位で辿る。
        /// レイヤー単位でパラメータを見たいので、クリップ列では足りない。
        /// </summary>
        private static IEnumerable<AnimatorController> EnumerateControllers(VRCAvatarDescriptor descriptor)
        {
            var seen = new HashSet<AnimatorController>();

            var layers = (descriptor.baseAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                .Concat(descriptor.specialAnimationLayers ?? System.Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>());

            foreach (var layer in layers)
            {
                if (layer.isDefault) continue;
                if (layer.animatorController is AnimatorController controller && seen.Add(controller))
                    yield return controller;
            }

#if MT_MA_PRESENT
            foreach (var merge in descriptor
                         .GetComponentsInChildren<nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator>(true))
            {
                if (merge == null || !merge.enabled) continue;
                if (merge.animator is AnimatorController controller && seen.Add(controller))
                    yield return controller;
            }
#endif
        }
    }
}
