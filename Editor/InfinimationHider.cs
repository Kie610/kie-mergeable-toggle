using System.Text;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>
    /// infinimation: 全頂点のデルタを +Infinity にしたブレンドシェイプを生成し、
    /// 0⇔100 で表示を切り替える。
    ///
    /// 頂点が非有限座標へ飛ぶのでプリミティブがクリップ段で破棄され、完全に消える。
    /// ランクの計上項目(boneCount / constraintsCount / materialCount / polyCount)は
    /// どれも動かず、シェーダにもプラットフォームにも依存しない。
    ///
    /// 初期非表示はシェイプのウェイトとしてそのままシリアライズできるので、
    /// 初期状態用のコンストレイントもマテリアル複製も要らない。
    ///
    /// NaN デルタは Unity が格納時に 0 へ潰すので、必ず +Infinity で作る(実測)。
    /// 代償は VRAM(1 トグルあたり頂点数×デルタ分)。ランクの計上対象ではない。
    ///
    /// AAO との整合: `blendShape.` カーブは AutoMergeSkinnedMesh の統合キーから
    /// 除外されるので統合グループを割らない。統合後もシェイプは改名されて生き残る。
    /// </summary>
    internal static class InfinimationHider
    {
        private const string ShapeNamePrefix = "MT_Hide_";

        public static bool CanApply(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            return mesh != null && mesh.vertexCount > 0;
        }

        public static HidePlan Apply(ToggleCandidate target, Transform root, bool initiallyHidden)
        {
            var plan = new HidePlan();

            foreach (var renderer in target.Renderers)
            {
                var shapeName = AddHideShape(renderer, target.Path);
                if (shapeName == null) continue;

                if (initiallyHidden)
                {
                    var index = renderer.sharedMesh.GetBlendShapeIndex(shapeName);
                    if (index >= 0) renderer.SetBlendShapeWeight(index, 100f);
                }

                plan.Toggled.Add((
                    EditorCurveBinding.FloatCurve(
                        AnimationUtility.CalculateTransformPath(renderer.transform, root),
                        typeof(SkinnedMeshRenderer),
                        "blendShape." + shapeName),
                    0f,
                    100f));
            }

            return plan;
        }

        /// <summary>
        /// 全頂点を +Infinity へ飛ばすブレンドシェイプを追加し、その名前を返す。
        /// メッシュは複製してから書き換える(非破壊)。
        /// </summary>
        private static string AddHideShape(SkinnedMeshRenderer renderer, string ownerPath)
        {
            var mesh = renderer.sharedMesh;
            if (mesh.vertexCount == 0) return null;

            var deltas = new Vector3[mesh.vertexCount];
            for (var v = 0; v < deltas.Length; v++) deltas[v] = Vector3.positiveInfinity;

            var newMesh = Object.Instantiate(mesh);
            newMesh.name = mesh.name;
            ObjectRegistry.RegisterReplacedObject(mesh, newMesh);

            var shapeName = MakeUniqueName(newMesh, ownerPath, renderer.name);
            // 法線・接線のデルタは 0(null)。消える面に陰影は意味を持たない。
            newMesh.AddBlendShapeFrame(shapeName, 100f, deltas, null, null);

            renderer.sharedMesh = newMesh;
            return shapeName;
        }

        private static string MakeUniqueName(Mesh mesh, string ownerPath, string rendererName)
        {
            var baseName = ShapeNamePrefix + Sanitize(ownerPath) + "_" + Sanitize(rendererName);
            var name = baseName;
            var suffix = 1;
            while (mesh.GetBlendShapeIndex(name) >= 0) name = baseName + "_" + suffix++;
            return name;
        }

        /// <summary>
        /// バインディング文字列 "blendShape.&lt;name&gt;" に埋め込むので、
        /// 区切りに使われうる文字は落としておく。
        /// </summary>
        private static string Sanitize(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            return builder.ToString();
        }
    }
}
