# lilToon 2.3.4 の設定別描画負荷――実測候補は屈折ぼかし、ファー、POM、テッセレーション、宝石

作成 2026-08-29。対象は VRChat PC 版で使われる Built-in Render Pipeline（BRP）と、
`avatar-packages/Packages/jp.lilxyzw.liltoon` に導入済みの lilToon **2.3.4** である。
GPU 時間はまだ測っていないため、負荷の実測値はすべて **未測定** とする。

## 結論

最初に実測する価値が高いのは、屈折ぼかし、ファー、POM、テッセレーション、宝石である。
屈折ぼかしは BRP の画面テクスチャを 2 パス合計 50 回参照する。ファーは専用パスと
Geometry Shader を使い、入力三角形あたり 8 / 14 / 26 頂点を生成する。POM はピクセルごとに
高さマップを反復参照する。テッセレーションは Hull / Domain Shader で形状を細分化し、宝石は
追加の事前パス、画面コピー、複数の画面・環境テクスチャ参照を持つ。

この順序は GPU 時間の順位ではない。コード上の仕事量から実測対象を絞るための順位である。
通常の影、反射、MatCap、メインカラー 2nd / 3rd も、塗り面積が大きい場合はテクスチャ参照の
増分が全ピクセルへ掛かるため、次点として測る。

## 確度と数え方

- `C:` ローカルの 2.3.4 のコード、`package.json`、またはパス構成で確認した事実
- `A:` ウェブ資料にある評価、またはコード構造からの負荷推定。実測未確認
- `U:` 今回の調査で解けなかった点

表の「追加テクスチャフェッチ」は、基本の不透明 lilToon がメインテクスチャを読む状態からの
増分である。明記しない限り 1 ピクセル、1 頂点、または 1 パスあたりのソース上のサンプラ呼び出しを
数えた。キャッシュ、コンパイラ最適化、Early-Z、オーバードロー、GPU アーキテクチャは反映しない。
したがって、フェッチ数は GPU 時間へ換算できない。

調査単位の確度は **C: 20 / A: 2 / U: 1** である。内訳は、機能表 22 行が C: 20 / A: 2、
項目別の公開実測を確認できなかったという表外の残件が U: 1 である。

## ウェブ調査で分かった範囲

`A:` 公式ドキュメントは、屈折、屈折ぼかし、ファー、ファー 2 パス、宝石を「高負荷」と表示し、
テッセレーションを「負荷が高め」「特にハイポリでは非常に高い」と説明する。また、ファーは
レイヤー数を増やすほど負荷が高くなる。両面描画は片面描画より重く、アルファマスクは追加の
テクスチャを読む分だけ負荷が上がると明記されている。

`A:` VRChat 向け最適化記事では、半透明は全ポリゴンの描画とブレンドが必要になりやすく、
シェーダーパスはドローコールを増やすと説明されている。別の公開ベンチマークには lilToon 2.3.4
全体の比較値があるが、機能条件が揃っておらず、設定項目ごとの比較には使えない。

`U:` 検索した範囲では、同じ lilToon マテリアルの機能を一つずつ切り替えた、再現可能な
項目別 GPU 時間の公開実測は確認できなかった。

ウェブ出典:

- lilToon 公式「基本設定」: https://lilxyzw.github.io/lilToon/ja_JP/base/base.html
- lilToon 公式「テッセレーション」: https://lilxyzw.github.io/lilToon/ja_JP/advanced/tessellation.html
- lilToon 公式「ファー設定」: https://lilxyzw.github.io/lilToon/ja_JP/advanced/fur.html
- lilToon 公式「アルファマスク」: https://lilxyzw.github.io/lilToon/ja_JP/color/alphamask.html
- lilToon 公式「最適化」: https://lilxyzw.github.io/lilToon/ja_JP/other/optimization.html
- lilToon 公式「はじめに」: https://lilxyzw.github.io/lilToon/ja_JP/first
- 作者リポジトリ: https://github.com/lilxyzw/lilToon
- VRChat アバター最適化メモ: https://dskjal.com/unity/vrchat-avatar-optimization
- VRChat Ask のシェーダー比較例: https://ask.vrchat.com/t/poiyomi-vs-mochi-any-performance-benchmarks/48809

## コードから数えた設定別の仕事量

「静的＋動的」は、`LIL_FEATURE_*` によるプリプロセッサ上の組み込みは静的だが、その内部に
`_Use*` などの実行時分岐が残ることを表す。通常版はビルド時に使われる機能の define を生成する
（`Editor/lilToonSetting.cs:582-669, 1028-1214`）。一方、同梱の Multi 版はキーワードで分け、
`Shader/Includes/lil_common.hlsl:27-50` で `_Use*` を定数化する。

ローカル出典の基点は `../../avatar-packages/Packages/jp.lilxyzw.liltoon/` である。表中で
`lil_common_*.hlsl` とだけ書いたファイルは、すべてその配下の `Shader/Includes/` にある。

| 項目 | 追加テクスチャフェッチ | 追加パス | 主に効く段 | 静的か動的か | 確度 | 出典 |
| --- | --- | --- | --- | --- | --- | --- |
| 屈折ぼかし | 画面テクスチャ **50**（横 17＋縦 33）。Smoothness テクスチャ使用時は各段に追加 | `GrabPass` 2 回＋`FORWARD_BLUR` 1 パス | フラグメント、画面コピー | 専用シェーダーは静的。ループ回数は静的 | C | `CustomShaderResources/BRP/DefaultRefractionBlur.lilblock:17-69`; `Shader/Includes/lil_pass_forward_refblur.hlsl:43-62`; `lil_common_frag.hlsl:1272-1294`; `lil_common_macro.hlsl:19` |
| ファー（2 パス） | ファー片面につき Noise / Mask が各 0〜1。Length Mask は入力三角形あたり頂点側 3 | 基本 Forward に `FORWARD_FUR_PRE` と `FORWARD_FUR` を追加。追加ライトでも 2 パス増 | Geometry、頂点、フラグメント | 専用シェーダーは静的。レイヤー数は動的 | C | `CustomShaderResources/BRP/DefaultFurTwoPass.lilblock:66-159,214-307`; `lil_common_vert_fur.hlsl:374-502`; `lil_common_frag.hlsl:402-453` |
| ファー（1 パス） | Noise / Mask が各 0〜1。Length Mask は入力三角形あたり頂点側 3 | 基本 Forward に `FORWARD_FUR` 1 パスを追加。追加ライトでも 1 パス増 | Geometry、頂点、フラグメント | 専用シェーダーは静的。`_FurLayerNum` は動的 | C | `Shader/lts_fur.shader:782-974`; `lil_common_vert_fur.hlsl:471-502`。生成頂点は三角形あたり 8 / 14 / 26 |
| POM（視差マップ） | 高さマップを反復。ループ上限式は `400 × _Parallax`、交差時に早期終了 | なし | フラグメント | `LIL_FEATURE_POM` は静的、反復と早期終了は動的 | C | `Shader/Includes/lil_common_functions.hlsl:584-615`（`LIL_POM_DETAIL=200`）; `lil_common_frag.hlsl:293-305` |
| テッセレーション | 0（この機能単体） | 通常版と同じパス数だが、Forward / Outline / ForwardAdd に Hull / Domain 段を追加 | Hull、Domain、頂点相当 | 専用シェーダーは静的。分割量は設定値で動的 | C | `CustomShaderResources/BRP/DefaultTessellation.lilblock:47-63,99-116,156-221`; 公式テッセレーション資料 |
| 宝石 | 画面 3＋環境キューブ 3＝**6**。Smoothness 使用時は 1 追加 | `GrabPass`＋`FORWARD_PRE` 1 パスを追加 | フラグメント、画面コピー | 専用シェーダーは静的。内部パラメーターは動的 | C | `CustomShaderResources/BRP/DefaultGem.lilblock:17-65`; `Shader/Includes/lil_pass_forward_gem.hlsl:213-257` |
| 輪郭線 | 頂点側 Width Mask 1、Vector Tex 1。フラグメント側 Outline Tex 0〜1 | `FORWARD_OUTLINE` 1、追加ライトごとに Outline 1、影生成時に Outline 1 | 頂点、フラグメント、描画コマンド | Outline シェーダー選択は静的 | C | `Shader/lts_o.shader:641-646`; `Shader/ltspass_opaque.shader:807-1035`; `lil_common_functions.hlsl:277,293`; `lil_common_frag.hlsl:360-398` |
| 半透明 2 パス | 基本機能と同じだが、裏面分も再実行 | `FORWARD_BACK` 1 パス | フラグメント、ブレンド、描画コマンド | 専用シェーダーは静的 | C | `Shader/lts_twotrans.shader:674`; `Shader/ltspass_transparent.shader:792-883` |
| 屈折 | 画面テクスチャ 1 | `GrabPass` 1 回。Forward の本数は通常と同じ | フラグメント、画面コピー | 専用シェーダーは静的 | C | `CustomShaderResources/BRP/DefaultRefraction.lilblock:16-64`; `lil_common_frag.hlsl:1272-1297` |
| 影（最大構成） | 基本計算 0。Strength / Blur / Border Mask 各 1、影色 3 枚は通常各 1、LUT 時は各 2で、最大 **9** | なし | フラグメント | 機能・テクスチャ有無は静的、`_UseShadow` と方式選択は動的 | C | `lil_common_frag.hlsl:915-1115`; LUT 1 色あたり 2 回は `lil_common_functions.hlsl:408-412` |
| 反射・光沢 | 環境キューブ 1。Smoothness / Metallic / Color が各 0〜1で、合計 **1〜4** | なし。ForwardAdd でも鏡面計算あり | フラグメント | 静的＋動的 | C | `lil_common_frag.hlsl:1314-1500`; 環境参照は `lil_common_functions.hlsl:1158` |
| メインカラー 2nd | Main 1＋Blend Mask 0〜1＋Dissolve 0〜1＋Noise 0〜1で **1〜4** | なし | フラグメント | 静的＋`_UseMain2ndTex` の動的分岐 | C | `lil_common_frag.hlsl:723-810`; Dissolve の参照は `lil_common_functions.hlsl:626-698` |
| メインカラー 3rd | 2nd と同型で **1〜4** | なし | フラグメント | 静的＋`_UseMain3rdTex` の動的分岐 | C | `lil_common_frag.hlsl:819-906` |
| MatCap 1st / 2nd | 各層で MatCap 1＋Blend Mask 0〜1＋専用 Normal 0〜1、つまり **1〜3** | なし | フラグメント | 静的＋動的 | C | `lil_common_frag.hlsl:1515-1632` |
| ラメ | Color 0〜1＋Shape 0〜1で **0〜2** | なし | フラグメント。Shape 使用時は微分つき参照と数学処理 | 静的＋動的 | C | `lil_common_frag.hlsl:1756-1803`; Shape 参照は `lil_common_functions.hlsl:1197-1264` |
| 発光 1st / 2nd | 各層で Map 0〜1＋Mask 0〜1＋Gradation 0〜1、つまり **0〜3** | なし | フラグメント | 静的＋動的 | C | `lil_common_frag.hlsl:1813-1950` |
| ノーマルマップ 1st / 2nd | 1st が 1、2nd が 1、2nd Scale Mask が 0〜1で合計 **1〜3** | なし | フラグメント | 静的＋動的 | C | `lil_common_frag.hlsl:563-600` |
| アルファマスク | **1** | なし | フラグメント | テクスチャ有無は静的、モードは動的 | C | `lil_common_frag.hlsl:457-475`; 公式アルファマスク資料 |
| AudioLink | Audio Texture または Local Map 1＋Mask 0〜1で **1〜2** | なし | フラグメント | 静的＋動的 | C | `lil_common_frag.hlsl:636-707` |
| 通常の視差マップ | **1** | なし | フラグメント | 機能は静的、`_UseParallax` は動的 | C | `lil_common_functions.hlsl:574-582`; `lil_common_frag.hlsl:293-305` |
| 半透明（1 パス） | 基本機能と同じ | なし | フラグメント、ブレンド、オーバードロー | シェーダー選択は静的 | A | 公式「基本設定」; VRChat アバター最適化メモ。実際の増分は重なりと塗り面積に依存し未測定 |
| 両面描画（Cull Off） | 基本機能と同じ | なし | ラスタライズ、フラグメント | Cull 状態は描画時設定 | A | 公式「基本設定」は片面の方が軽いと明記。増えるピクセル数は形状と視点に依存し未測定 |

パス数はシーンのライト数でも変わる。BRP の基本シェーダー自体が `FORWARD_ADD` を持つため、
追加ライトが当たるたびに基本面を再描画する。輪郭線とファーは対応する ForwardAdd 専用パスも
持つので、ライト数が増えるほど差が広がる。これは設定単体の固定コストとして表へ足していない。

## 重そうな上位 8 項目

1. **屈折ぼかし** — `C:` 画面テクスチャ 50 フェッチ、GrabPass 2 回、追加 Forward パスを持つ。
2. **ファー 2 パス** — `C:` Geometry Shader の生成量に加え、ファー用 Forward を 2 回実行する。
3. **ファー 1 パス** — `C:` 入力三角形あたり最大 26 頂点を生成し、専用 Forward パスを足す。
4. **POM** — `C:` ピクセルごとに高さマップを反復参照し、上限式が `400 × _Parallax` になる。
5. **テッセレーション** — `C:` Hull / Domain Shader で入力形状を細分化する。公式も高負荷とするが実時間は未測定。
6. **宝石** — `C:` GrabPass、追加事前パス、画面 3＋環境 3 フェッチを持つ。
7. **輪郭線** — `C:` 本体とは別の Forward / ForwardAdd / ShadowCaster を増やす。
8. **影の最大構成** — `C:` 3 層＋各種マスク＋LUT 影色で、最大 9 フェッチを全対象ピクセルへ追加する。

上位 6 件は処理構造が明確に大きい。7、8 位はメッシュの面積、ライト数、影用カメラ、使用する
マスク数で逆転しやすい。GPU 時間の順位として公開してはいけない。

## 方法と限界

ウェブでは公式、作者リポジトリ、VRChat 最適化記事、公開ベンチマークを検索した。ローカルでは
`package.json` の版を確認した後、BRP の `.lilblock` と生成済み `.shader` からパスを数え、
`lil_pass_*.hlsl`、`lil_common_frag.hlsl`、`lil_common_functions.hlsl`、頂点・ファー関連ファイルで
`LIL_FEATURE_*` の範囲と `LIL_SAMPLE_*` / `LIL_GET_*` の呼び出しを追った。

限界は三つある。第一に、ソース上の参照回数は GPU の実行時間ではない。第二に、動的分岐は
マテリアル値、ピクセル、ライト、VR の視点で実行量が変わる。第三に、GrabPass、透明の
オーバードロー、キャッシュ効率、命令数、帯域、ドライバ最適化は単純なフェッチ数へ入らない。
この文書から言えるのは「どれを先に測るか」までである。

## 実測の計画

実測は **1 体を画面いっぱいに映す構図**で行う。ピクセル段を見るためである。10 体を小さく
並べる既存の構図では、フレームの塗り面積が約 1% しかなく、設定差が出ない。

基準マテリアルを一つ作り、項目を一つだけ有効にした対を交互に測る。頂点数、メッシュ、画角、
解像度、ライト、AA、影、その他の lilToon 設定は固定する。ファーのレイヤー数、POM の深さ、
テッセレーション最大分割数のように連続値を持つ項目は、既定値と上限側を別系列にする。

マテリアル設定を変えるとシェーダーバリアントが変わるため、**比較のたびにウォームアップを
入れる**。最初のコンパイルやロードを計測へ混ぜない。Profiler では GPU Frame Time を主指標とし、
Draw Calls、SetPass Calls、Triangles / Vertices、CPU Main / Render Thread も併記する。

判定は、反復ごとの対の差 `d = 機能あり − 機能なし` を集め、**`|平均 d|` が `d` の半レンジを
超え、かつ符号が全反復で揃ったときだけ有意**とする。条件を満たさない項目は「差なし」ではなく
「判定不能」と書く。最初の実測対象は上位 6 件とし、その後に輪郭線と影の最大構成を測れば、
大きな構造差と通常機能の境界を最小の組数で確認できる。
