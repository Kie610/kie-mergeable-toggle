using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Kie.MergeableToggle
{
    /// <summary>
    /// アバタールートに置く設定コンポーネント。
    /// ビルド時、既存アニメーターレイヤーの m_IsActive トグルを検出し、
    /// infinimation(全頂点デルタ +Infinity のブレンドシェイプ)へ変換して
    /// AAO のメッシュ統合を可能にする。
    /// </summary>
    [AddComponentMenu("Kie/kieMergeableToggle")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VRCAvatarDescriptor))]
    [HelpURL("https://github.com/Kie610/kie-mergeable-toggle")]
    public sealed class MergeableToggle : MonoBehaviour, IEditorOnly
    {
        /// <summary>変換の有効/無効</summary>
        public bool enableConversion = true;

        /// <summary>
        /// 非表示のあいだ、対象の中のコンポーネントも無効化する。
        ///
        /// 隠蔽機構はメッシュを見えなくするだけなので、これを切るとトグル対象の中にある
        /// PhysBone は揺れ続け、パーティクルは出続け、コンタクトは反応し続ける。元の
        /// m_IsActive と同じタイミングで m_Enabled を落として挙動を揃える。
        ///
        /// 対象の「外」(Armature 配下など)にあるコンポーネントは元から m_IsActive の
        /// 影響を受けないので、ここでも触らない。
        /// </summary>
        public bool disableComponentsWhenHidden = true;

        /// <summary>変換から除外するトグル対象のパス(クリーン候補はデフォルトで変換対象)</summary>
        public List<string> excludedPaths = new List<string>();

        /// <summary>警告付き候補のうち、明示的に変換対象へ含めるパス(デフォルトは対象外)</summary>
        public List<string> forceIncludedPaths = new List<string>();
    }
}
