using System.Collections.Generic;
using UnityEditor;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>
    /// 「対象を隠すために何をアニメーションすればよいか」を表す計画。
    ///
    /// 隠蔽シェイプ・コンポーネントの m_Enabled・PhysBone の停止が、どれも
    /// 「表示時の値と非表示時の値を持つバインディング」に落ちるので、共通層はこれだけ
    /// 受け取れば元の m_IsActive カーブを書き換えられる。
    /// </summary>
    internal sealed class HidePlan
    {
        /// <summary>元のトグルカーブに追従させるバインディング</summary>
        public readonly List<(EditorCurveBinding binding, float visible, float hidden)> Toggled = new();

        public bool IsEmpty => Toggled.Count == 0;
    }
}
