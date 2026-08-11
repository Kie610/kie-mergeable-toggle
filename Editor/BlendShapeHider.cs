using System.Collections.Generic;
using System.Text;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;

namespace Kie.MergeableToggle.Editor
{
    /// <summary>
    /// 案1: 全頂点を畳むブレンドシェイプを生成し、0⇔100 で表示を切り替える。
    ///
    /// ランクの計上項目（boneCount / constraintsCount / materialCount / polyCount）を
    /// 一切増やさず、Mobile のシェーダーホワイトリスト制約にも掛からない。
    /// 初期非表示はブレンドシェイプのウェイトとしてそのままシリアライズできるので、
    /// NaNimation のような初期状態用コンストレイントも要らない。
    ///
    /// 幾何的な限界と、寄せ先の選び方:
    ///
    /// ブレンドシェイプはスキニングの前に適用されるため、全頂点を同一座標へ寄せても
    /// 頂点ごとにウェイトが違えばスキニング後の位置は一致せず、三角形は面積を持ったまま残る。
    ///
    /// 寄せ先には「その頂点に効いているボーンのうち階層が最も深いものの原点」を使う。
    /// 子ボーンの原点は親ボーンの座標系における固定点（子の回転に依存しない）なので、
    /// 影響ボーンが親子ペアに収まっていれば、どちらのボーンが運んでも同じ点に着地する。
    /// つまりポーズによらず崩れない。
    ///
    /// ドミナントボーン（最大ウェイトのボーン）の原点を使うと、これが成立しない。
    /// 例えば太もも 50% / すね 50% の頂点を股関節へ送ると、すね側の寄与が
    /// 「すねが股関節を運んだ位置」になり、膝を曲げた瞬間に飛び出す。
    ///
    /// 残る限界: 隣り合う頂点の最深ボーンが異なると、その関節間に細い面が残る。
    /// 複数のボーンチェーンに跨るスカートのようなメッシュで顕著。
    /// この量は <see cref="MaxCollapsedEdgeLength"/> で測って警告できる。
    /// </summary>
    internal static class BlendShapeHider
    {
        private const string ShapeNamePrefix = "MT_Hide_";

        public static bool CanApply(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            return mesh != null && mesh.vertexCount > 0;
        }

        public static HidePlan Apply(ToggleCandidate target, Transform root, bool initiallyHidden, bool toAxis)
        {
            var plan = new HidePlan();

            foreach (var renderer in target.Renderers)
            {
                var shapeName = AddCollapseShape(renderer, target.Path, toAxis);
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
        /// 全頂点をドミナントボーンの原点へ寄せるブレンドシェイプを追加し、その名前を返す。
        /// メッシュは複製してから書き換える(非破壊)。
        /// </summary>
        private static string AddCollapseShape(SkinnedMeshRenderer renderer, string ownerPath, bool toAxis)
        {
            var mesh = renderer.sharedMesh;
            var vertices = mesh.vertices;
            if (vertices.Length == 0) return null;

            var deltas = ComputeDeltas(mesh, renderer, vertices, toAxis);

            var thickness = MaxCollapsedThickness(mesh, vertices, deltas);
            Debug.Log($"[MergeableToggle] thickness\t{ownerPath}\t{renderer.name}\t{thickness:F4}");
            if (thickness > ThicknessWarningMeters)
            {
                Debug.LogWarning(
                    $"[MergeableToggle] '{ownerPath}' の {renderer.name} は畳んだあとも " +
                    $"最大 {thickness:F3}m の太さの面が残ります。複数のボーンチェーンに跨る" +
                    "上着やスカートで起きやすく、体から出て見えることがあります。" +
                    "気になる場合はこのトグルだけ NaNimation を選んでください。" +
                    // この指標は静止時の残りしか見ていない。タイツのように静止時は
                    // ほぼ消えていて着座で跳ね上がる破綻は捕まえられない。
                    "なお、静止時に消えていてもポーズを付けると出てくる場合があります" +
                    "(肌に密着して関節を跨ぐ衣装)。その場合はシェイプ(軸)を試してください。");
            }

            var newMesh = Object.Instantiate(mesh);
            newMesh.name = mesh.name;
            ObjectRegistry.RegisterReplacedObject(mesh, newMesh);

            var shapeName = MakeUniqueName(newMesh, ownerPath, renderer.name);
            // 法線・接線のデルタは 0(null)。畳んだ面は面積を持たないので陰影は意味を持たない。
            newMesh.AddBlendShapeFrame(shapeName, 100f, deltas, null, null);

            renderer.sharedMesh = newMesh;
            return shapeName;
        }

        /// <summary>
        /// 寄せ先の選択で考慮するウェイトの下限(その頂点の最大ウェイトに対する比)。
        /// これを入れないと、わずかなウェイトが乗った深いボーン(遠くの指や物理ボーンの末端)に
        /// 寄せ先を乗っ取られる。実測では 0.01 の絶対値閾値だけだと最悪の結果になった。
        /// </summary>
        private const float RelativeWeightThreshold = 0.25f;

        /// <summary>数値ノイズを落とすための絶対値下限</summary>
        private const float AbsoluteWeightThreshold = 0.01f;

        /// <summary>畳んだあとの面の太さがこれを超えるなら警告する(メートル)</summary>
        private const float ThicknessWarningMeters = 0.05f;

        internal static Vector3[] ComputeDeltas(
            Mesh mesh, SkinnedMeshRenderer renderer, Vector3[] vertices, bool toAxis)
        {
            var deltas = new Vector3[vertices.Length];
            var bindposes = mesh.bindposes;
            var bonesPerVertex = mesh.GetBonesPerVertex().ToArray();
            var weights = mesh.GetAllBoneWeights().ToArray();

            // スキンされていないメッシュは1点へ寄せれば厳密に潰れる(スキニングが挟まらないため)
            if (bindposes.Length == 0 || bonesPerVertex.Length != vertices.Length)
            {
                var center = mesh.bounds.center;
                for (var v = 0; v < vertices.Length; v++) deltas[v] = center - vertices[v];
                return deltas;
            }

            // bindposes[i] はメッシュのバインド空間 → ボーン i のローカル空間。
            // その逆行列で原点を送れば、ボーン i の原点をバインド空間で表した位置になる。
            var boneOrigins = new Vector3[bindposes.Length];
            for (var i = 0; i < bindposes.Length; i++)
                boneOrigins[i] = bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero);

            var depths = ComputeBoneDepths(renderer, bindposes.Length);
            var axisEnds = toAxis ? ComputeAxisEnds(renderer, boneOrigins) : null;

            var w = 0;
            for (var v = 0; v < vertices.Length; v++)
            {
                var count = bonesPerVertex[v];

                var maxWeight = 0f;
                for (var i = 0; i < count; i++)
                {
                    var bw = weights[w + i];
                    if (bw.boneIndex >= 0 && bw.boneIndex < boneOrigins.Length && bw.weight > maxWeight)
                        maxWeight = bw.weight;
                }

                var threshold = Mathf.Max(AbsoluteWeightThreshold, maxWeight * RelativeWeightThreshold);
                var chosen = -1;
                var chosenDepth = int.MinValue;
                var chosenWeight = 0f;

                for (var i = 0; i < count; i++)
                {
                    var bw = weights[w + i];
                    if (bw.boneIndex < 0 || bw.boneIndex >= boneOrigins.Length) continue;
                    if (bw.weight < threshold) continue;

                    var depth = depths[bw.boneIndex];
                    // 最も深いボーンの原点(= 影響ボーン群の末端の関節)を選ぶ。
                    // 同じ深さならウェイトの大きい方。
                    if (depth > chosenDepth || (depth == chosenDepth && bw.weight > chosenWeight))
                    {
                        chosen = bw.boneIndex;
                        chosenDepth = depth;
                        chosenWeight = bw.weight;
                    }
                }

                w += count;
                // 有効なウェイトが無い頂点は動かしようがないので据え置く(隠れ残りとして現れる)
                if (chosen < 0)
                {
                    deltas[v] = Vector3.zero;
                    continue;
                }

                var origin = boneOrigins[chosen];
                var goal = axisEnds == null
                    ? origin
                    // 軸上へ射影したうえで、関節原点からのオフセットを選んだボーンの
                    // ウェイトで縮める。ウェイトが混ざる頂点(= 関節の近傍)ほど、
                    // ポーズ非依存な唯一の点である関節原点へ寄る。
                    : origin + (ProjectOnAxis(vertices[v], origin, axisEnds[chosen]) - origin) * chosenWeight;

                deltas[v] = goal - vertices[v];
            }

            return deltas;
        }

        private static Vector3 ProjectOnAxis(Vector3 vertex, Vector3 origin, Vector3 end)
        {
            var axis = end - origin;
            var lengthSq = axis.sqrMagnitude;
            if (lengthSq < 1e-12f) return origin;
            return origin + axis * Mathf.Clamp01(Vector3.Dot(vertex - origin, axis) / lengthSq);
        }

        /// <summary>
        /// 各ボーンの軸線分の終点(バインド空間)。最初の子ボーンの原点を使い、
        /// 子が無ければ親からの向きを同じ長さだけ延長する(末端ボーン)。
        /// どちらも取れないボーンは原点のままなので、その頂点は一点へ畳まれる。
        /// </summary>
        private static Vector3[] ComputeAxisEnds(SkinnedMeshRenderer renderer, Vector3[] boneOrigins)
        {
            var ends = (Vector3[])boneOrigins.Clone();
            var bones = renderer.bones;
            var indexOf = new Dictionary<Transform, int>();
            for (var i = 0; i < boneOrigins.Length && i < bones.Length; i++)
                if (bones[i] != null) indexOf[bones[i]] = i;

            for (var i = 0; i < boneOrigins.Length; i++)
            {
                var bone = i < bones.Length ? bones[i] : null;
                if (bone == null) continue;

                var found = false;
                foreach (Transform child in bone)
                {
                    if (!indexOf.TryGetValue(child, out var childIndex)) continue;
                    ends[i] = boneOrigins[childIndex];
                    found = true;
                    break;
                }

                if (!found && bone.parent != null && indexOf.TryGetValue(bone.parent, out var parentIndex))
                    ends[i] = boneOrigins[i] + (boneOrigins[i] - boneOrigins[parentIndex]);
            }

            return ends;
        }

        /// <summary>各ボーンの階層の深さ。参照できないボーンは -1。</summary>
        private static int[] ComputeBoneDepths(SkinnedMeshRenderer renderer, int boneCount)
        {
            var depths = new int[boneCount];
            var bones = renderer.bones;
            for (var i = 0; i < boneCount; i++)
            {
                var bone = i < bones.Length ? bones[i] : null;
                if (bone == null)
                {
                    depths[i] = -1;
                    continue;
                }

                var depth = 0;
                for (var t = bone.parent; t != null; t = t.parent) depth++;
                depths[i] = depth;
            }

            return depths;
        }

        /// <summary>
        /// 畳んだあとに残る面の太さ(最小の垂線長)の最大値。
        ///
        /// 辺の長さではなく太さで測ることが重要。共線に潰れた三角形は辺が長くても
        /// 面積ゼロで描画されないため、辺の長さで評価すると判断を誤る。
        /// </summary>
        private static float MaxCollapsedThickness(Mesh mesh, Vector3[] vertices, Vector3[] deltas)
        {
            var collapsed = new Vector3[vertices.Length];
            for (var v = 0; v < vertices.Length; v++) collapsed[v] = vertices[v] + deltas[v];

            var max = 0f;
            var triangles = mesh.triangles;
            for (var t = 0; t + 2 < triangles.Length; t += 3)
            {
                var a = collapsed[triangles[t]];
                var b = collapsed[triangles[t + 1]];
                var c = collapsed[triangles[t + 2]];

                var doubleArea = Vector3.Cross(b - a, c - a).magnitude;
                if (doubleArea <= 0f) continue;

                var longest = Mathf.Max((a - b).magnitude, Mathf.Max((b - c).magnitude, (c - a).magnitude));
                if (longest > 0f) max = Mathf.Max(max, doubleArea / longest);
            }

            return max;
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
