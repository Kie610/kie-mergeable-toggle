# 隠蔽機構 — 仕様と根拠

最終更新: 2026-08-25

## 解きたい問題

SMR を含むゲームオブジェクトの `m_IsActive` をアニメーションでオンオフしている構造を、
別の機構へ置き換える。目的は SkinnedMeshRenderer 数の削減。

AAO の自動メッシュ統合 (`Editor/Processors/TraceAndOptimize/AutoMergeSkinnedMesh.cs`) は
メッシュを activeness でグルーピングしてから統合するため、トグルが異なるメッシュ同士は
統合されない。統合後の SMR 数 ≒ 異なるトグルの数 + 常時オン群。トグルを別機構へ移せば、
そのメッシュは常時オン群に合流できる。

ポリゴン数は削減ツールで、マテリアル数は統合ツールで、いずれも非破壊で解決できる。
**表示切替条件が一致しない SMR だけは他のどのツールでも統合できない。** そこがこの
ツールの存在理由なので、SMR を減らすために他のランク項目を悪化させては本末転倒になる。

要件の優先順位:

1. トグル挙動が保たれること
2. パフォーマンスランクの計上項目に悪影響を与えないこと
3. Mobile (Android / Quest) でも適用できること

## 共通層がやること

### 実行順: Modular Avatar より後

`InPhase(BuildPhase.Transforming).AfterPlugin("nadena.dev.modular-avatar")`。

MA Merge Animator が合流させたコントローラも、MA がリアクティブに生成したトグルも、
合流後の仮想アニメーター上では手書きトグルと区別が付かないので、そのまま候補になる。

0.1.0-alpha までは「手書きレイヤーの状態のまま処理したい」として MA より**前**に
走らせていたが、それだと Merge Animator 経由のトグルが一切見えず、そういう構成の
アバターで何も変換されなかった。実測 (Shinano Variant、FX をどこへ置くか):

| 構成 | 検出件数 |
|---|---|
| ディスクリプタの FX レイヤー | 9 |
| Merge Animator (Absolute) | 0 → **9** |
| Merge Animator (Relative、ルート直下) | 0 → **9** |
| Merge Animator (Relative、子オブジェクト、relativePathRoot=ルート) | 0 → **9** |

変換本体は `AnimationIndex` 経由で候補抽出とカーブ書き換えを行い、descriptor を直接
見ていないので、順序を後ろへ動かしても手を入れる箇所は無かった (変更は `Configure()`
の1語)。なお VRC Parameter Compressor は `BuildPhase.Optimizing` なので無関係。

### 編集時と ビルド時の乖離

インスペクタの一覧は編集時に作るので、ビルド時にトグルを生成するツールのぶんは
まだ存在せず、出せない。実測では Avatar Menu Creator (Narazaka) が該当し、
MA Object Toggle も同様。`m_IsActive` を生成しうる NDMF ツールは無数にあるため、
**一覧を完全にすることは原理的にできない**。

乖離は両方向に出る。ビルド中に階層を張り替えるパッケージ (AvatarPoseSystem 等) では
編集時とビルド時でパスが別物になり、**一覧に出たのに変換されない**候補も生じる
(実測: `APS_HandleEx/HandleMesh`)。実際に変換したパスはビルドログへ
出力するので突合できる。

Merge Animator が `Relative` のときはカーブパスがコンポーネント基準なので、
`relativePathRoot` (未設定ならコンポーネントの位置) をアバタールート相対へ直して
前置してから照合する。

### その他

機構によらず共通:

- 候補の抽出と絞り込み (入れ子トグルは外側が優先、内側は落とす)
- 対象を常時アクティブ化し、元の `m_IsActive` カーブを機構のバインディングへ書き換える
- `rootBone` / `localBounds` / `updateWhenOffscreen` を正規化して AAO の
  `CategorizationKey` を揃える (統合を可能にする本体)。`rootBone` は変換対象以外の
  SMR が最も多く共有している値へ合わせる (MA Mesh Settings 等でアバター内に既に
  合意がある場合、そこへ参加する)。1 つも無ければ Humanoid の Hips へ倒す

機構ごとの差分は `HidePlan` を返すバックエンドに閉じている。

### 配下コンポーネントの無効化

`disableComponentsWhenHidden` (既定オン) は、変換対象 SMR、Transform、IEditorOnly を除く
配下コンポーネントを `(アバタールート相対パス, 型)` でまとめ、`m_Enabled` を表示と同じ
1⇔0 カーブへ付け替える。編集時のクリーン判定とビルド時の生成判定は、
`ComponentDisabler.Analyze` の同じ結果を使う。

| 判定 | ビルド時 | インスペクタの警告 |
|---|---|---|
| `m_Enabled` が true で、下記ガードに非該当 | 無効化カーブを生成 | なし |
| 編集時点で `m_Enabled` が false | 生成せず、ビルド警告 | なし (元から動かない) |
| 同一 GameObject に同じ型が複数 | 生成せず、ビルド警告 | `<型名> (同型複数)` |
| `m_Enabled` が既にアニメーションされている | 生成せず、ビルド警告 | `<型名> (m_Enabled アニメーション済み)` |
| `m_Enabled` を持たない | 生成しない | `<型名>` |

既存アニメーションの判定には、トグル検出と同じクリップ走査で収集した
`m_Enabled` の `(パス, 型)` を使う。別のクリップ走査は行わない。ビルド警告は次の形式で、
複数件は `; ` で連結する。

`[MergeableToggle] skipped component disabling under '<トグルのパス>': '<コンポーネントのパス>' (<型名>): <理由>`

## 隠しかた: infinimation

トグル対象レンダラーの全頂点デルタを `+Infinity` にしたブレンドシェイプを生成し、
`m_IsActive` カーブを `blendShape.<名前>` の 0⇔100 カーブへ書き換える。
頂点が非有限座標へ飛ぶのでプリミティブがクリップ段で破棄され、完全に消える。

| ボーン | コンストレイント | PB | マテリアル | ポリゴン | 消えかた | シェーダ依存 |
|---|---|---|---|---|---|---|
| ±0 | ±0 | ±0 | 統合される | ±0 | 完全に消える | 無し |

- 生成は `Mesh.AddBlendShapeFrame(name, 100f, deltas(+∞), null, null)`。normal/tangent は null。
  **NaN デルタは Unity が格納時に 0 へ潰すので、必ず +Infinity で作る**(実測)
- メッシュは `Instantiate` + `RegisterReplacedObject` で複製してから書き換える(非破壊)
- ブレンドシェイプ数はランクの計上対象ではない。代償は VRAM
  (1 トグルあたり頂点数×デルタ分。20k 頂点で 1MB 弱)

### AAO との整合

- `blendShape.` カーブは `GetAnimationLocationsForRendererAnimation` が除外するので、
  統合グループを割らない (AAO 1.9.17 のソース確認)
- AAO は NaN デルタを +∞ へ置換して infinimation を保全する処理を持つ
  (`MeshInfo2.cs`。NDMF ツールチェーンがこの技法を使う前提のコメント付き)
- アニメーション付きシェイプは AutoFreezeNonAnimatedBlendShape の対象外。
  InternalAutoFreezeMeaninglessBlendShape はデルタ全ゼロのみが対象なので ∞ は非該当
- 統合後はシェイプが `AAO_Merged_<n>_<元メッシュ名>__MT_Hide_…` へ改名され、
  カーブもそれに追随する (実測で確認)

### bounds が壊れない理由

変換時に `updateWhenOffscreen = false` へ正規化しているため、Unity はスキニング結果から
bounds を再計算しない。シリアライズされた localBounds をそのまま使うので、頂点が
+∞ へ飛んでも bounds は有限のまま。**UWO を true にしたレンダラーがあると破綻し得る。**

### 退役した4機構 (0.5.0-alpha)

シェイプ(関節) / シェイプ(軸) / UVタイル破棄 / NaNimation の 4 つを持っていたが、
infinimation がすべての面で上回ったため 1 本化した。実装は git 履歴にある。

| 退役した機構 | infinimation に対して劣っていた点 |
|---|---|
| シェイプ(関節) | 畳み残しが見える (静止残留型・ポーズ依存型の破綻) |
| シェイプ(軸) | 同上。ポーズ依存型を軽減できるだけで、消えはしない |
| UVタイル破棄 | lilToon 専用・PC 限定・15 枠上限。初期非表示でマテリアル複製が要る |
| NaNimation | boneCount が増える (Shinano +197 / MUMUS_all +480) |

機構が 1 つになったことで、機構ごとの使い分けを決める MethodAdvisor
(畳み残しの面積を静止時と着座ポーズで実測して割り当てる仕掛け) も不要になり削除した。

## 初期非表示の扱い

初期状態で非表示のトグルは、アニメーターが最初のフレームを適用するまで隠蔽が効かない。

- 隠蔽ブレンドシェイプのウェイト 100 を Renderer へシリアライズする
- 無効化対象の配下コンポーネントは、シリアライズ時点で `m_Enabled=false` にする
- 単独トグル所有の PhysBone は、シリアライズ時点で `enabled=false` にする
- 共有 PhysBone グループは、全 owner が初期非表示なら `MT_PBStop` レイヤーの
  `Stopped` を既定ステートにする

このため、初期状態用のコンストレイントも、マテリアル複製も要らない。

### WD ON でも成立する

Write Defaults ON は「レイヤーが触っていないプロパティを、アニメーター開始時点の
シリアライズ値へ書き戻す」挙動。書き戻し先が我々の仕込んだ値そのものなので、
WD ON でも初期非表示は保たれる。

## PhysBone の停止

`disablePhysBonesWhenHidden` は、チェーンを消費する全 Renderer が変換対象トグルで
隠れるアーマチュア側 PhysBone だけを停止する。単独トグル所有なら、そのトグルの
隠蔽カーブへ `VRCPhysBone.m_Enabled` の 1⇔0 を追加する。

`disableSharedPhysBonesWhenHidden` は、複数トグルで共有される PB を所有トグル集合ごとに
まとめ、FX の末尾へ `MT_PBStop <n>` レイヤーを作る。各 owner の
`MT_Hidden/<トグルのパス>` AAP がすべて 0.5 より大きいと `Stopped` へ入り、いずれかが
0.5 より小さくなると `Active` へ戻る。両ステートは Write Defaults ON、遷移時間 0。
パラメータは同期せず、Expression Parameters を消費しない。

AAP は同じ AnimatorController の中でしか駆動できないため、owner の `m_IsActive`
クリップが 1 本でも FX 以外に属するグループは生成しない。また、生成する
`MT_Hidden/<トグルのパス>` と同名のパラメータが FX に既にある場合は、衝突する owner を
含むグループ全体を生成しない。どちらも対象 owner またはパラメータ名をビルド警告へ出す。
編集時無効、既存の
`m_Enabled` アニメーション、同一 GameObject の複数 PB、消費者なし、変換対象外の
Renderer にも消費される PB は、単独・共有とも保守側へ倒して触らない。

## 実測 (AAO 込み、Unity 2022.3.22f1)

| アバター | 構成 | SMR | MatSlots | Bones | Tris |
|---|---|---|---|---|---|
| Shinano | 変換なし | 11 | 14 | 272 | 140034 |
| Shinano | infinimation | **2** | **6** | 272 | 140034 |
| MUMUS_all | 変換なし | 21 | 36 | 453 | 233251 |
| MUMUS_all | infinimation | **4** | **18** | 453 | 233251 |

ボーンとポリゴンは変換なしと同値、つまりアバター素のまま。退役した NaNimation は
同条件でボーンが 272→469 / 453→933 に増えていた。

この表の MatSlots は SkinnedMeshRenderer と MeshRenderer を合算している。現行の
`MTPbE2E` は SkinnedMeshRenderer だけを数えるため、同じビルドでも Shinano 5 /
MUMUS_all 16 と出る。差は各アバターが 1 個だけ持つ MeshRenderer のぶん
(Shinano 1 スロット、MUMUS_all 2 スロット) で、統合結果そのものは変わっていない。

Play (Av3Emulator) と PC 実機でも、トグル往復・初期非表示・カリング・ランク表示に
問題が無いことを確認済み (2026-08-25)。

**Quest (モバイル GPU) でも消えることを実機で確認済み (2026-08-25)。**
検証は `DevProjectQuest` で Milfy_QuestMobile へ検証用ボックスを足したアバターを
ビルドし、トグル対象が消えて常時表示のメッシュが無傷であることを目視で確かめた。
遠近のカリングと Performance ランク表示は未報告。

## 前提となる確認済み事実

### ランクの計上項目 (SDK の閾値アセット)

`com.vrchat.base/Runtime/VRCSDK/Dependencies/VRChat/Resources/Validation/Performance/StatsLevels/`

| 項目 | PC Excellent | PC Good | PC Medium | PC Poor | Quest Excellent | Quest Good | Quest Medium | Quest Poor |
|---|---|---|---|---|---|---|---|---|
| skinnedMeshCount | 1 | 2 | 8 | 16 | 1 | 1 | 2 | 2 |
| meshCount | 4 | 8 | 16 | 24 | 1 | 1 | 2 | 2 |
| materialCount | 4 | 8 | 16 | 32 | 1 | 1 | 2 | 4 |
| boneCount | 75 | 150 | 256 | 400 | 75 | 90 | 150 | 150 |
| constraintsCount | 100 | 250 | 300 | 350 | 30 | 60 | 120 | 150 |
| polyCount | 32000 | 70000 | 70000 | 70000 | 7500 | 10000 | 15000 | 20000 |
| textureMegabytes | 40 | 75 | 110 | 150 | 10 | 18 | 25 | 40 |
| physBone componentCount | 4 | 8 | 16 | 32 | 4 | 8 | 16 | 32 |
| physBone transformCount | 16 | 64 | 128 | 256 | 0 | 16 | 32 | 64 |
| physBone colliderCount | 4 | 8 | 16 | 32 | 4 | 8 | 16 | 32 |
| physBone collisionCheckCount | 32 | 128 | 256 | 512 | 32 | 128 | 256 | 512 |

**ブレンドシェイプ数・頂点数・メッシュメモリは計上対象に存在しない。**
polyCount は三角形数であり、頂点を潰しても複製しても変わらない。

### Mobile のシェーダー制限

`AvatarValidation.ShaderWhiteList` の10種のみ許可される (`VRChat/Mobile/*`)。
lilToon も Poiyomi も含まれない。10種のソースを全て確認したが **`_Cutoff` を公開して
いるものは一つも無い**。したがって Mobile ではシェーダー側で隠す機構が全て使えない。

### 縮退三角形と NaN の実コスト差は小さい

頂点を1点へ潰した三角形は面積ゼロになりラスタライズ段で破棄される。NaN の三角形は
クリップ段で破棄される。**どちらも塗りのコストは発生しない。** 残るのは頂点処理の
コストで、そこは同等 (ブレンドシェイプ評価 vs ボーン行列)。

## 没案とその理由

| 案 | 不成立の理由 |
|---|---|
| ボーン部分木の scale 0 | 成立条件は「その部分木が他メッシュに一切使われていない」こと。実測 (5体62レンダラー) で頂点が部分木内で完結する割合は Shinano のスカート・タイツで 0%、Selena のスカートで 27%。高い値が出たのは元々問題にならない小物だけ |
| 単一 NaN ボーンを全メッシュに追加 | Unity は Transform へ NaN スケールを書けない。加えて `Mesh.SetBoneWeights` が 4 ボーン/頂点に切り詰めるので5本目のウェイトを持てない (実測 nanVerts=0) |
| マテリアルスワップ / アルファ・カットオフ | Mobile 不可 (上記シェーダー制限)。PC では UVタイル破棄で足りる |
| 静的削除 | トグルにならない |
| AAO `RemoveMeshByUVTile` への相乗り | 静的削除なのでトグルにならない |
| インデックスバッファ破棄 | アニメーションのバインディング対象外 |
| 頂点カラー破棄 | lilToon / Poiyomi に経路が無い。カスタムシェーダは方針外 |
| 遠方への吹き飛ばしシェイプ | 回転差の大きいポーズで巨大三角形が視界を掠める。ワールドの far clip 依存で品質保証ができない |
| 統合グループをカーブで揃える | 実装して実測したが効果ゼロ (SMR 3 / MatSlots 7 のまま)。`GetAnimationLocationsForRendererAnimation` は `blendShape.` で始まるプロパティを明示的に除外する (`AutoMergeSkinnedMesh.cs:503`) ため、シェイプ方式のレンダラーの `RendererAnimationLocations` はもともと空で、前提が成り立っていなかった。真因は未特定 |

**第5の隠蔽 primitive は存在しない。** アニメーション可能で描画に影響する要素は
{activeness (統合を壊す)・Transform (ボーン系)・blendshape・マテリアル float・
マテリアルスワップ} で全て。スクリプト不可なのでインデックスバッファ系は動かせない。
blendshape で任意ポーズの縮退を満たすには三角形の3頂点が同一ウェイトである必要が
あり (スキニングの線形性)、複数チェーン跨ぎでは原理的に不可能。

## 指標選びの教訓

この件で指標を3回間違えている。辺の長さ (共線に潰れた三角形は見えない) →
面の太さ (静止時しか見ない・太さ 1cm の帯は普通に見える) → ポーズを付けたときの
可視面積。間接的な指標で判断すると、もっともらしい誤結論に到達する。

可視面積も「体表の外に出るか」までは測れていない。関節集約は面積が小さくても帯が
露出し、軸射影は面積が大きくても手足の内部に留まる。**最終判断は目視。**

## 開発メモ

- AAO の `MeshInfo2` は `-nographics` に非対応。AAO を通すベンチは `-batchmode` のみで
  走らせる
- 計測足場は `DevProject/Assets/_MTBench/Editor/MTBench.cs` (`BenchMixedAAO` が上表を出す)
