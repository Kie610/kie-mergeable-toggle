using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>
    /// 案3: lilToon の UV タイル破棄(UDIM Discard)で隠す。
    ///
    /// メッシュの UV3 へ「4x4 グリッドのどのタイルに居るか」を書き込み、
    /// そのタイルに対応する <c>_UDIMDiscardRow{row}_{col}</c> を 0⇔1 で切り替える。
    ///
    /// <c>_UDIMDiscardMode = 0</c>(頂点モード)は頂点シェーダで positionCS を NaN に
    /// するので、NaNimation と全く同じ「プリミティブがクリップ段で破棄される」挙動を、
    /// ボーンを1本も増やさずに得られる。ランクの計上項目はどれも動かない。
    ///
    /// セットアップ用の3プロパティ(Compile/Mode/UV)は shader_feature キーワードでは
    /// なく CBUFFER 内の float uniform で、頂点シェーダ側も実行時 if で見ている。
    /// つまり定数カーブで駆動できるので、マテリアルを複製する必要が無い。
    /// 複製すると AAO がマテリアルスロットを統合できなくなるため、これは大きい。
    /// 例外は初期非表示のトグルだけで、そこはシリアライズ値が要るので複製する。
    ///
    /// 判定に UV3 を使うので UV0 には一切触らない。UV3 を持たないメッシュには GPU が
    /// 0 を供給し、タイル(0,0)は決してフラグが立たないので、UV3 を持たないレンダラーが
    /// 同じマテリアルを共有していても巻き込まれない。
    ///
    /// 制約:
    /// - lilToon(または同等のプロパティを持つシェーダ)専用。lilToon Lite は
    ///   プロパティ自体を持たないので <see cref="CanApply"/> が弾く。
    /// - Mobile のシェーダーホワイトリストに該当機能を持つものが無いので PC 限定。
    /// - タイルは 16 個。(0,0) は「UV3 を持たないメッシュ」の居場所なので使えず、
    ///   実際に使えるのは 15 個。それを超えるトグルには適用できない。
    /// - メッシュが既に UV3 を使っている場合は適用できない。
    /// </summary>
    internal static class UVTileDiscardHider
    {
        /// <summary>(0,0) は UV3 を持たないメッシュの居場所なので使わない</summary>
        public const int UsableTileCount = 15;

        private const string CompileProperty = "_UDIMDiscardCompile";
        private const string ModeProperty = "_UDIMDiscardMode";
        private const string UVProperty = "_UDIMDiscardUV";

        /// <summary>判定に使う UV チャネル。UV0 を汚さないため一番奥から取る。</summary>
        private const int Channel = 3;

        public static bool CanApply(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0) return false;
            // ponytail: UV3 が埋まっていたら諦める。UV2/UV1 への退避は、実際に
            // 衝突するアバターが出てきてから足す(アバターメッシュでは稀)。
            if (mesh.HasVertexAttribute(VertexAttribute.TexCoord3)) return false;

            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) return false;

            foreach (var material in materials)
                if (material == null || !material.HasProperty(CompileProperty))
                    return false;

            return true;
        }

        /// <param name="tileIndex">0 以上 <see cref="UsableTileCount"/> 未満。タイル (0,0) は避けて割り当てる。</param>
        /// <param name="tileRenderers">
        /// この方式を使う全トグルのレンダラー。自分のタイルのプロパティを
        /// 「この方式を使う全レンダラー」へ付けるために要る。
        ///
        /// AAO の CategorizationKey には RendererAnimationLocations が含まれるので、
        /// レンダラーごとに animate するプロパティが違うと別グループへ分かれて統合されない。
        /// 全レンダラーへ全タイル分のカーブを付けるとキーが揃う。
        /// 各メッシュは自分のタイルにしか居ないため、他のタイルのフラグが立っても
        /// 何も消えない。統合後は1つのレンダラーの全マテリアルへプロパティが及ぶので、
        /// 統合前にその状態を先取りしていることになる。
        /// </param>
        public static HidePlan Apply(
            ToggleCandidate target, Transform root, bool initiallyHidden, int tileIndex,
            IEnumerable<SkinnedMeshRenderer> tileRenderers)
        {
            var plan = new HidePlan();

            // tileIndex 0 → タイル(1,0)。(0,0) を飛ばすため +1 する。
            var linear = tileIndex + 1;
            var col = linear % 4;
            var row = linear / 4;

            foreach (var renderer in target.Renderers)
                WriteTileChannel(renderer, col, row);

            foreach (var renderer in tileRenderers)
            {
                var path = AnimationUtility.CalculateTransformPath(renderer.transform, root);

                plan.Toggled.Add((Bind(path, $"material._UDIMDiscardRow{row}_{col}"), 0f, 1f));

                // セットアップもカーブで与える(= マテリアルを書き換えない)。
                // 全レンダラーへ同じ集合を付けるので CategorizationKey は揃ったまま。
                plan.Constant.Add((Bind(path, "material." + CompileProperty), 1f));
                plan.Constant.Add((Bind(path, "material." + ModeProperty), 0f)); // 0 = Vertex
                plan.Constant.Add((Bind(path, "material." + UVProperty), Channel));
            }

            return plan;
        }

        /// <summary>
        /// 初期非表示のトグルは、アニメーターが最初のフレームを適用するまでの間だけ
        /// セットアップ値が無く見えてしまう。ここだけはマテリアルを複製して
        /// シリアライズ値で立てておく(そのマテリアルはスロット統合を諦める)。
        /// </summary>
        public static void SetInitiallyHidden(ToggleCandidate target, int tileIndex)
        {
            var linear = tileIndex + 1;
            var tileProperty = $"_UDIMDiscardRow{linear / 4}_{linear % 4}";

            foreach (var renderer in target.Renderers)
            {
                var materials = renderer.sharedMaterials;
                var replaced = new Material[materials.Length];

                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null) continue;

                    var material = Object.Instantiate(source);
                    material.name = source.name;
                    ObjectRegistry.RegisterReplacedObject(source, material);

                    material.SetFloat(CompileProperty, 1f);
                    material.SetFloat(ModeProperty, 0f);
                    material.SetFloat(UVProperty, Channel);
                    material.SetFloat(tileProperty, 1f);

                    replaced[i] = material;
                }

                renderer.sharedMaterials = replaced;
            }
        }

        private static EditorCurveBinding Bind(string path, string property) =>
            EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), property);

        /// <summary>
        /// 全頂点の UV3 へタイル中心 (col + 0.5, row + 0.5) を書く。
        /// lilUDIMDiscard は floor を取って升目を決めるので、中心なら境界の誤差で
        /// 隣のタイルへ漏れることが無い。メッシュは複製してから書き換える。
        /// </summary>
        private static void WriteTileChannel(SkinnedMeshRenderer renderer, int col, int row)
        {
            var mesh = renderer.sharedMesh;

            var newMesh = Object.Instantiate(mesh);
            newMesh.name = mesh.name;
            ObjectRegistry.RegisterReplacedObject(mesh, newMesh);

            var center = new Vector2(col + 0.5f, row + 0.5f);
            var uvs = new List<Vector2>(mesh.vertexCount);
            for (var i = 0; i < mesh.vertexCount; i++) uvs.Add(center);
            newMesh.SetUVs(Channel, uvs);

            renderer.sharedMesh = newMesh;
        }
    }
}
