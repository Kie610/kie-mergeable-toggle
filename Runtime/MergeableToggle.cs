using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Kie.MergeableToggle
{
    /// <summary>隠蔽機構のバックエンド。強み弱みが異なるので用途で選ぶ。</summary>
    public enum HideMethod
    {
        /// <summary>
        /// 全頂点をドミナントボーンの原点へ寄せるブレンドシェイプを生成し、0⇔100 で切り替える。
        /// ランクの計上項目に影響せず Mobile でも使えるが、隠れた頂点は消滅せず骨格上へ畳まれる。
        /// </summary>
        BlendShape = 0,

        /// <summary>
        /// ブレンドシェイプだが、頂点を関節へ集めるのではなくボーンの軸線分へ吸着させる。
        ///
        /// 関節へ集めると、隣り合う関節の間に「親ボーンの座標系に固定されて動かない帯」が
        /// 残る。周囲の体表はポーズで逃げるので、股関節や膝では帯だけが取り残されて露出する。
        /// 軸へ吸着させれば畳まれた面は手足の内部に沿って残り、手足と一緒に動く。
        ///
        /// 代償として、複数ボーンに跨る頂点の厳密なポーズ安定性は失われる
        /// (厳密に一致するのは関節原点だけ)。そのため関節原点からのオフセットを
        /// 選んだボーンのウェイトで縮め、ウェイトが混ざる頂点ほど関節原点へ寄せている。
        ///
        /// 実測(着座ポーズでの可視面積)では、タイツが最悪 699cm² → 355cm²、
        /// スラックスが 1536cm² → 453cm² に下がる一方、複数のボーンチェーンに跨る
        /// スカート・コートでは逆に悪化する。前者向け。
        /// </summary>
        BlendShapeAxis = 2,

        /// <summary>
        /// ボーンを複製してスケールを NaN⇔1 で切り替える。プリミティブがクリップ段で破棄される。
        /// boneCount と constraintsCount が増えるため、ランクを詰める用途には使えない。
        /// </summary>
        NaNimation = 1,

        /// <summary>
        /// lilToon の UV タイル破棄。メッシュの UV0 を空きタイルへ移し、
        /// そのタイルの破棄フラグを 0⇔1 で切り替える。
        ///
        /// 頂点モードでは頂点シェーダが positionCS を NaN にするので、NaNimation と
        /// 同じ「完全に消える」挙動をボーンを1本も増やさずに得られる。
        /// ランクの計上項目はどれも動かない。
        ///
        /// 制約: lilToon 系シェーダ専用、PC 限定(Mobile のホワイトリストに該当機能なし)、
        /// 使えるタイルは 15 個まで。
        /// </summary>
        UVTileDiscard = 3,
    }

    /// <summary>
    /// トグル単位の機構の上書き。
    ///
    /// 実測では、ブレンドシェイプで畳んだあとに面が残るのは
    /// 「複数のボーンチェーンに跨る上着・スカート」に集中している。
    /// そこだけ NaNimation にすれば、ボーンの増分をその数件に限定できる。
    /// </summary>
    [System.Serializable]
    public sealed class MethodOverride
    {
        public string path;
        public HideMethod method;
    }

    /// <summary>
    /// アバタールートに置く設定コンポーネント。
    /// ビルド時、既存アニメーターレイヤーの m_IsActive トグルを検出し、
    /// 選択した機構へ変換して AAO のメッシュ統合を可能にする。
    /// </summary>
    [AddComponentMenu("Kie/kieMergeableToggle")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VRCAvatarDescriptor))]
    [HelpURL("https://github.com/Kie610/kie-mergeable-toggle")]
    public sealed class MergeableToggle : MonoBehaviour, IEditorOnly
    {
        /// <summary>変換の有効/無効</summary>
        public bool enableConversion = true;

        /// <summary>隠蔽機構のバックエンド(既定値)</summary>
        public HideMethod hideMethod = HideMethod.BlendShape;

        /// <summary>既定値と違う機構を使うトグル</summary>
        public List<MethodOverride> methodOverrides = new List<MethodOverride>();

        /// <summary>そのトグルに実際に使う機構</summary>
        public HideMethod MethodFor(string path)
        {
            foreach (var o in methodOverrides)
                if (o != null && o.path == path) return o.method;
            return hideMethod;
        }

        public void SetMethodFor(string path, HideMethod? method)
        {
            methodOverrides.RemoveAll(o => o == null || o.path == path);
            if (method.HasValue)
                methodOverrides.Add(new MethodOverride { path = path, method = method.Value });
        }

        /// <summary>
        /// 非表示のあいだ、対象の中のコンポーネントも無効化する。
        ///
        /// 隠蔽機構はメッシュを見えなくするだけなので、これを切ると PhysBone は揺れ続け、
        /// パーティクルは出続け、コンタクトは反応し続ける。元の m_IsActive と同じ
        /// タイミングで m_Enabled を落として挙動を揃える。
        /// </summary>
        public bool disableComponentsWhenHidden = true;

        /// <summary>変換から除外するトグル対象のパス(クリーン候補はデフォルトで変換対象)</summary>
        public List<string> excludedPaths = new List<string>();

        /// <summary>警告付き候補のうち、明示的に変換対象へ含めるパス(デフォルトは対象外)</summary>
        public List<string> forceIncludedPaths = new List<string>();

        /// <summary>
        /// UV タイル破棄で、初期非表示トグルのマテリアル複製を省く。
        ///
        /// UV タイル破棄のセットアップ値はアニメーターが最初のフレームを適用するまで
        /// 効かないため、初期非表示のトグルはそれまで見えてしまう。通常はそこだけ
        /// マテリアルを複製してシリアライズ値で立てておくが、複製したマテリアルは
        /// AAO がスロット統合できなくなる。
        ///
        /// これを有効にすると複製を省いてスロットを1つ余分に詰められる代わりに、
        /// ワールド入室時やアバター切替時に、初期非表示のメッシュが一瞬見える。
        /// 見える長さはアニメーターの初期化タイミング次第で、1フレームとは限らない。
        /// </summary>
        public bool skipInitiallyHiddenMaterialClone;
    }
}
