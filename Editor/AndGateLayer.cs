using System;
using System.Collections.Generic;
using nadena.dev.ndmf.animator;
using UnityEditor.Animations;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>
    /// 「所有トグルが全部非表示のときだけ Stopped、どれか 1 つでも表示なら Active」の
    /// 2 状態レイヤーを FX へ足す。条件は各トグルの AAP `MT_Hidden/&lt;パス&gt;` の AND。
    /// 共有 PhysBone の停止 (`MT_PBStop`) と隠れたスロットの差し替え (`MT_SlotOff`) が
    /// 同じ形なので共通化している。
    /// </summary>
    internal static class AndGateLayer
    {
        public static void Add(
            VirtualAnimatorController fx,
            string layerName,
            IReadOnlyList<string> ownerParameters,
            bool defaultStopped,
            Action<VirtualClip> fillActive,
            Action<VirtualClip> fillStopped)
        {
            var activeClip = VirtualClip.Create(layerName + " Active");
            var stoppedClip = VirtualClip.Create(layerName + " Stopped");
            fillActive(activeClip);
            fillStopped(stoppedClip);

            // 他プラグインが正の優先度で追加したレイヤーよりも後ろへ置く。
            var layer = fx.AddLayer(new LayerPriority(int.MaxValue), layerName);
            layer.DefaultWeight = 1f;
            var stateMachine = layer.StateMachine;
            var active = stateMachine.AddState("Active", activeClip);
            var stopped = stateMachine.AddState("Stopped", stoppedClip);
            stateMachine.DefaultState = defaultStopped ? stopped : active;
            active.WriteDefaultValues = true;
            stopped.WriteDefaultValues = true;

            var toStopped = VirtualStateTransition.Create();
            toStopped.Duration = 0f;
            toStopped.ExitTime = null;
            toStopped.SetDestination(stopped);
            foreach (var parameter in ownerParameters)
            {
                toStopped.Conditions = toStopped.Conditions.Add(new AnimatorCondition
                {
                    mode = AnimatorConditionMode.Greater,
                    parameter = parameter,
                    threshold = 0.5f,
                });
            }
            active.Transitions = active.Transitions.Add(toStopped);

            foreach (var parameter in ownerParameters)
            {
                var toActive = VirtualStateTransition.Create();
                toActive.Duration = 0f;
                toActive.ExitTime = null;
                toActive.SetDestination(active);
                toActive.Conditions = toActive.Conditions.Add(new AnimatorCondition
                {
                    mode = AnimatorConditionMode.Less,
                    parameter = parameter,
                    threshold = 0.5f,
                });
                stopped.Transitions = stopped.Transitions.Add(toActive);
            }
        }
    }
}
