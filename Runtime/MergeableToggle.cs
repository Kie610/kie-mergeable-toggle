using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Kie.MergeableToggle
{
    /// <summary>
    /// アバタールートに置く設定コンポーネント。
    /// ビルド時、既存アニメーターレイヤーの m_IsActive トグルを検出し、
    /// MA Mesh Cutter (NaNimation) へ変換して AAO のメッシュ統合を可能にする。
    /// </summary>
    [AddComponentMenu("Kie/Mergeable Toggle")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VRCAvatarDescriptor))]
    [HelpURL("https://github.com/Kie610/mergeable-toggle")]
    public sealed class MergeableToggle : MonoBehaviour, IEditorOnly
    {
        /// <summary>変換の有効/無効</summary>
        public bool enableConversion = true;

        /// <summary>変換から除外するトグル対象のパス(クリーン候補はデフォルトで変換対象)</summary>
        public List<string> excludedPaths = new List<string>();

        /// <summary>警告付き候補のうち、明示的に変換対象へ含めるパス(デフォルトは対象外)</summary>
        public List<string> forceIncludedPaths = new List<string>();
    }
}
