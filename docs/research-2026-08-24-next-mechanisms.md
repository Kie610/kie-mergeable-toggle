# 新隠蔽機構の探索結果 (2026-08-24)

後続セッションがこの文書だけで実装・検証を進められるように書いた自己完結の引き継ぎ。
確定度は C(確認済み) / A(仮定) / U(未解決) で示す。

## 背景と目的

既存 4 機構の弱点: シェイプ(関節/軸)は畳み残しが見える、UVタイル破棄は lilToon/PC 限定で
15 枠上限、NaNimation は boneCount が激増する(Shinano +197)。負荷を増やさず AAO 統合を
保ったまま完全に消える機構を探索した。結果、**2 つの新機構が primitive レベルで成立**。

## 実測サマリ (C)

環境: Unity 2022.3.22f1 Play モード、D3D11、skinWeights=FourBones、GPU スキニング設定オン。
対象: `DevProject/Assets/000_Avatars_Variant/Milfy Variant.prefab` の Body_base(18440頂点)/
Baretop(17567)/ Body(12166)。1024x1024 描画の画素数と色差分(閾値 |Δrgb|>6)で判定。

| テスト | 結果 |
|---|---|
| ∞シェイプ weight 0 | ベースラインと画素・色とも完全一致(0×∞ 汚染なし) |
| ∞シェイプ weight 100 / 50 | 画素数 0 = 完全消滅。中間値でも消えたまま |
| NaN デルタのシェイプ | **格納時に 0 へ潰される**(readback=0)。不成立 |
| ε ウェイト+NaN ボーン scale=1 | 完全一致(4影響使い切り頂点の最小ウェイト置換 2133 個含む) |
| 同 scale=NaN | 画素数 0 = 完全消滅 |
| 同 scale=0 | ほぼ不変 = **初期非表示を scale 0 で表現できない** |

## 機構A: infinimation(∞ ブレンドシェイプ)— 本命

トグル対象レンダラーの全頂点デルタを `+Infinity` にしたシェイプを生成し、
`m_IsActive` カーブを `blendShape.<名前>` の 0⇔100 カーブへ書き換える。

- 生成: `Mesh.AddBlendShapeFrame(name, 100f, deltas(+∞), null, null)`。normal/tangent は null。
  **NaN は Unity が 0 に潰すので必ず +∞ で作る** (C)
- 初期非表示: シェイプ weight 100 をレンダラーへシリアライズするだけ。WD ON でも成立
  (書き戻し先が仕込んだ値)。NaNimation の「Transform へ NaN を書けない」問題が構造的に無い (C)
- ランク: ボーン±0・コンストレイント±0。ブレンドシェイプ数は計上対象外 (C、既存調査)
- AAO 整合 (C、1.9.17 ソース確認):
  - `blendShape.` カーブは `GetAnimationLocationsForRendererAnimation` が除外
    (`AutoMergeSkinnedMesh.cs:503`)→ 統合グループを割らない
  - AAO は NaN デルタを +∞ へ置換して infinimation を保全する処理を持つ
    (`MeshInfo2.cs:889-898`。NDMF ツールチェーンが本技法を予定しているというコメント付き)
  - アニメーション付きシェイプは AutoFreezeNonAnimatedBlendShape の対象外。
    InternalAutoFreezeMeaninglessBlendShape はデルタ全ゼロのみ対象なので ∞ なら非該当
  - MergeSkinnedMesh はシェイプを改名保持(RenameToAvoidConflict)しカーブも追随
- VRAM: 1 トグルあたり頂点数×デルタ分(20k 頂点で 1MB 弱)。ランク計上外 (C)
- d4rk の NaNimation 既知問題(ネームプレート位置)はボーンを触らないため発生しない (A)

## 機構B: 微小ウェイト共有 NaN ボーン — 現行 NaNimation の置き換え候補

d4rkAvatarOptimizer が実証済みの方式("an extra bone per original mesh ... very low weight")。

- トグルあたり 1 本のプロキシボーンを Hips 配下へ追加(bindpose 恒等)、スケールを NaN⇔1
- ウェイト注入: 影響 <4 の頂点は既存を (1-ε) 倍して ε=0.001 を追記。4 本使用中の頂点は
  最小ウェイトを除去し再正規化して ε を入れる。BoneWeight1 はウェイト降順で格納
- NaN カーブは `AnimationCurve` を `AddKey` で構築(コンストラクタは NaN キーを捨てる)。
  Transform への直接代入は Play モードでも弾かれる (C)
- コスト: +1 ボーン/トグル(現行の貪欲被覆複製方式は Shinano で +197)
- 弱点: 初期非表示を scale 0 で表現できない(実測)。初期非表示トグルは現行方式へ
  フォールバックするか機構A に任せる (C)
- AAO はボーンウェイトのしきい値間引き・再正規化を一切持たないので ε は生き残る (C)
- U: Quest/VR Low が skinWeights=2 だとの記述あり(コミュニティ観測、未確認)。真なら
  ε(最小ウェイト)が落ちて NaN が届かない頂点が出る。**現行 NaNimation にも同種のリスク**
  (付け替えたウェイトが 3/4 位のとき)

## 副産物の確定事項 (C)

- AAO 統合キーは 17 項目 AND(`AutoMergeSkinnedMesh.cs:846-926`)。カーブ由来の
  `RendererAnimationLocations` は「property 名 + defaultValue + AnimationLocation 集合」で、
  AnimationLocation の等価は **AnimatorState インスタンス同一性**(クリップは比較外)。
  過去の「カーブで揃える案が効果ゼロ」の真因はここ。UVタイル群を統合したい場合は
  同一ステート内バインディング + マテリアル静的値の一致が必要
- マテリアルスワップ(PPtr カーブ)は自動統合の入口で一律拒否(`IsAnimatedForbidden`)。不成立
- `HasUnsupportedComponents`: レンダラーの GameObject に余計なコンポーネントが残ると統合対象外
- lilToon 2.3.4: UDIM Discard は 1.x と同仕様。IDMask(`_IDMask1..8`、CBUFFER float)が
  追加 8 枠として実在するがモバイル系 API では機構ごと無効。頂点破棄経路はこの 2 つ+
  アウトライン消去で全部。lilToon のシェーダ設定最適化はコンパイルゲートの静的値を見るため、
  `_UDIMDiscardCompile` 等は静的非ゼロで焼くこと(現行実装は満たす)
- AAO に activeness 違い統合は無い(Issue #1526 オープンのまま)

## 実装の一手 (提案)

1. `InfinimationHider`(`HidePlan` バックエンド 1 つ)を追加。CanApply はほぼ常に真
2. AAO 込み E2E(Shinano/MUMUS ベンチ再現)で SMR/MatSlots/Bones を実測
3. 成立すればシェイプ(関節/軸)・UVタイル・NaNimation の退役を検討 → MethodAdvisor 縮小
   (機構が減ること自体が使いやすさの改善)

## 未検証 (U)

- U1: AAO 込み E2E での統合数(機構A/B とも)
- U2: VRC クライアント実機(PC)での挙動
- U3: Quest 実機(モバイル GPU)での ∞ 頂点・skinWeights の挙動
- U4: 機構B の見た目品質の目視(Cardigan 級 = 4影響率 33%・最小ウェイト最大 0.24 の衣装)

## 検証ハーネス

`DevProject/Assets/_MTLab/`(Editor/MTLabProbe.cs + MTLabDriver.cs)。結果は
`DevProject/MTLabOut/`(probe_play.txt と PNG 群)。実行:

```
Unity.exe -batchmode -projectPath D:\GitHub_WorkSpace\VRC\DevProject -executeMethod MTLabProbe.RunPlay -logFile <log>
```

罠: -quit を付けない(Driver が Exit する)/ 編集モードの手動 Camera.Render はスキニングを
更新しない(Play モード必須)/ batchmode では WaitForEndOfFrame が発火しない/
Unity は終了時に Temp/ を消すので成果物は別フォルダへ/ Unity.exe は即座に制御を返すので
完了はファイル出現かプロセス監視で待つ/ sanity チェック(既存シェイプ 100 で画素が変わる)を
必ず入れる。

## 参照

- Obsidian Vault(D:\Obsidian\Obsidian Vault)の 2026-08-24 ノート群:
  `調査/…infinimationと微小ウェイトNaNボーンをUnity実測で確認`、
  `調査/…AAOの統合キーはAnimatorState同一性とdefaultValueで決まる`、
  `調査/…infinimation - 無限大ブレンドシェイプ隠蔽をAAOが既に保全している`、
  `調査/…lilToon 2.3.4 の頂点破棄経路は UDIM・IDMask・アウトライン消去の3つで全部`、
  `調査/…d4rkのNaNimationはトグルあたり1ボーンの微小ウェイト方式`、
  `罠/…lilToonのシェーダ設定最適化はコンパイルゲートの静的値だけを見る`、
  `罠/…Unityバッチの描画検証はPlayモード必須でWaitForEndOfFrameは発火しない`
- d4rkAvatarOptimizer README「Use NaNimation Toggles」/ AAO Issue #1526
