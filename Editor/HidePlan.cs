using System.Collections.Generic;
using UnityEditor;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>
    /// バックエンドが「対象を隠すために何をアニメーションすればよいか」を返すための計画。
    ///
    /// どのバックエンドも結局は「表示時の値と非表示時の値を持つバインディングの集合」に
    /// 落ちるので、共通層はこれだけ受け取れば元の m_IsActive カーブを書き換えられる。
    /// </summary>
    internal sealed class HidePlan
    {
        /// <summary>元のトグルカーブに追従させるバインディング</summary>
        public readonly List<(EditorCurveBinding binding, float visible, float hidden)> Toggled = new();

        /// <summary>トグルクリップ内で定数にしておくバインディング(NaNimation の初期状態解除など)</summary>
        public readonly List<(EditorCurveBinding binding, float value)> Constant = new();

        public bool IsEmpty => Toggled.Count == 0;
    }
}
