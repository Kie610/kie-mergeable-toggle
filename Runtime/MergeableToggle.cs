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

        /// <summary>
        /// 非表示のあいだ、その衣装のためだけに存在するアーマチュア側の PhysBone も止める。
        ///
        /// 衣装の揺れボーンはアーマチュア側に付いているため、元の m_IsActive トグルでも
        /// <see cref="disableComponentsWhenHidden"/> でも止まらず、隠れたまま CPU を使い続ける。
        /// これを有効にすると、PhysBone のチェーンを使うレンダラーが全部そのトグルで
        /// 隠れる場合に限り、同じタイミングで m_Enabled を落とす。
        /// 素体と共有しているボーン(胸・尻尾など)は判定で除外されるので止まらない。
        ///
        /// 再表示時、PhysBone はレスト位置から揺れ直す(Unity の仕様。隠れている間の
        /// 状態は保持されない)。
        /// </summary>
        public bool disablePhysBonesWhenHidden = true;

        /// <summary>
        /// 複数のトグルで共有されるアーマチュア側 PhysBone も、所有トグルが全部
        /// 非表示のときに止める。専用のレイヤーとアニメーターパラメータを FX へ生成する
        /// (同期パラメータは使わないので Expression Parameters は消費しない)。
        /// disablePhysBonesWhenHidden が OFF のときは効果なし。
        /// </summary>
        public bool disableSharedPhysBonesWhenHidden = true;

        /// <summary>変換から除外するトグル対象のパス(クリーン候補はデフォルトで変換対象)</summary>
        public List<string> excludedPaths = new List<string>();

        /// <summary>警告付き候補のうち、明示的に変換対象へ含めるパス(デフォルトは対象外)</summary>
        public List<string> forceIncludedPaths = new List<string>();
    }
}
