# Agent handoff v1

updated: 2026-08-25
repo: D:/GitHub_WorkSpace/VRC/Packages/com.kie.kie-mergeable-toggle (origin = github.com/Kie610/kie-mergeable-toggle)
work_branch: main
upstream: origin/main (2026-08-19 時点で同期済み・未再確認)
base: main@3907539
goal: 手書きのメッシュトグルを AAO が統合できる隠しかたへ機械的に変換する

## State

complete:
- C: 0.4.0-alpha 公開済み。一覧を持ち主ごとの折りたたみにした回 (検出・変換のロジックは無変更)
- C: 変換の実装一式 (検出 `ToggleScanner` / 変換 `MergeableTogglePlugin` / 表示 `MergeableToggleInspector`)
- C: 隠しかたを infinimation の 1 本にした (0.5.0-alpha、2026-08-25)。`InfinimationHider` だけを残し、シェイプ(関節)・シェイプ(軸)・UVタイル破棄・NaNimation と `HideMethod` / `MethodOverride` / `MethodAdvisor` / `skipInitiallyHiddenMaterialClone` / `HidePlan.Constant` を削除。インスペクタは機構ポップアップと「自動で割り当てる」を失い、一覧と除外だけになった。**破壊的変更** (利用者は居ないというユーザー判断のもとで実施)。README・Docs/hiding-mechanisms.md・AGENTS.md の公開契約も更新済み
- C: 新機構探索 (2026-08-24)。infinimation (∞デルタのブレンドシェイプ) と微小ウェイト共有 NaN ボーン (d4rk 方式、+1ボーン/トグル) の 2 案が primitive レベルで実測成立。NaN デルタ案とマテリアルスワップ案は不成立が確定。詳細・設計案・AAO 整合条件は `Docs/research-2026-08-24-next-mechanisms.md` (これだけで再開可能な自己完結文書)

verified:
- C: 2026-08-11 — evidence: status=PASS; kind=runtime; command=NDMF ビルド一式 (当時の E2E スクリプト、詳細は handoff-history.md); environment=Unity 2022.3.22f1 batchmode/DevProject; scope=実アバター 2 体への E2E 変換 (MUMUS_all SMR 21→4・ボーン 453→933 / Shinano SMR 11→2・ボーン 272→469、ポリゴン不変、検出はシーン内 10 体で 80 候補); counts=passed=2, failed=0, skipped=0, not-run=0
- C: 2026-08-24 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -executeMethod MTLabProbe.RunPlay; environment=Unity 2022.3.22f1 Play モード/D3D11/skinWeights=4; scope=Milfy Variant 3 メッシュで ∞シェイプ (w0 無害・w100/50 完全消滅) と εNaN ボーン (scale1 色差分ゼロ・NaN 完全消滅) の描画実測。AAO 込み E2E と実機は対象外; counts=passed=6, failed=0, skipped=0, not-run=0
- C: 2026-08-24 — evidence: status=PASS; kind=build+unit; command=Unity.exe -batchmode -quit (コンパイル) / -executeMethod InfinimationCheck.Run; environment=Unity 2022.3.22f1 batchmode/DevProject; scope=DevProject 全体のコンパイル (error CS 0 件) と、生成したシェイプのデルタ読み戻し (Infinimation=+∞ のまま格納される・既存 BlendShape 経路は有限値のまま)。AAO 込み E2E と実機は対象外; counts=passed=2, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=InfinimationE2E.Run (Unity MCP 経由で実行); environment=Unity 2022.3.22f1 Editor/DevProject/AAO Trace and Optimize 付与; scope=Shinano・MUMUS_all を 変換なし/BlendShape/Infinimation/NaNimation の 4 通りでベイクし統合数を比較。Infinimation は BlendShape と SMR・MatSlots・Bones・Tris が完全一致 (Shinano SMR 11→2・MatSlots 14→6・ボーン 272 不変 / MUMUS_all SMR 21→4・MatSlots 36→18・ボーン 453 不変)。NaNimation は同条件でボーン 272→469・453→933。結果は DevProject/MTLabOut/e2e_infinimation.txt; counts=passed=8, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=Play (Av3Emulator) でユーザー目視 + ベイク結果の機械検査; environment=Unity 2022.3.22f1 Play/DevProject/Shinano Variant (infinimation・AAO 付き); scope=(1) 9 トグルが完全に消える (2) 破綻ポリゴン・ちらつき無し (3) ポーズを付けても残存無し (5) ベイク後の SMR bounds が全件有限・UWO=false・隠蔽シェイプ 9 個が AAO 統合後も生存 (初期非表示 2 件はウェイト 100 で焼かれている); counts=passed=4, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=VRCSDK Build & Test (ユーザーが実施・目視報告); environment=VRChat クライアント PC 版/Shinano Variant (infinimation・AAO 付き); scope=鏡でのトグル往復・初期非表示の入室直後の状態・遠近でのカリング・SDK の Performance ランクの 4 点を確認し、いずれも問題なし。Quest (モバイル GPU) は対象外; counts=passed=4, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=build+runtime; command=Unity MCP 経由で Client.Resolve + RequestScriptCompilation → E2E 再実行 (Editor 占有中のため batchmode は未実施); environment=Unity 2022.3.22f1 Editor/DevProject; scope=1 本化後の再コンパイル (error 0 件。`Library/ScriptAssemblies` の DLL に InfinimationHider が在り MethodAdvisor / HideMethod が消えていることをバイナリ走査で確認) と、E2E の再実行で 1 本化前と同一の数値 (Shinano SMR 11→2・MatSlots 14→6・ボーン 272 / MUMUS_all SMR 21→4・MatSlots 36→18・ボーン 453、変換数 9 と 19); counts=passed=2, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=build; command=Unity.exe -batchmode -quit -projectPath DevProject; environment=Unity 2022.3.22f1 batchmode (Editor を閉じた状態); scope=1 本化後の DevProject 全体のコンパイル。error CS 0 件、exit 0; counts=passed=1, failed=0, skipped=0, not-run=0

not-run:
- U: U4 新機構の Quest 実機 (モバイル GPU で ∞ 頂点がどう扱われるか)。PC は 2026-08-25 に確認済み
- U: U5 εNaN ボーン方式は未着手のまま棚上げ (infinimation で足りたため。研究文書に設計案は残っている)

## Decisions

- C: MA Mesh Cutter を生成して MA に処理させる案 (方針B) は不成立 (2026-08-11、詳細は履歴)
- C: Modular Avatar への依存は持たない (0.1.0-alpha で除去)
- C: 変換後は rootBone→Hips / localBounds→合併値 / UWO→false へ正規化する
- C: infinimation は NaN でなく必ず +∞ デルタで作る (Unity が NaN を 0 に潰す。実測)
- C: `disableComponentsWhenHidden` は衣装の PhysBone を止められない (2026-08-25 実測)。
  Shinano の PB/コライダー 72 個はすべて `Armature/…` 配下にあり、トグル対象
  (`Cloth_skirt` 等のメッシュのオブジェクト) の配下に無いため。**変換前の
  `m_IsActive` トグルでも同じ**なので退行ではない。隠した衣装の PB を止めるには
  メッシュと骨側 PB の対応付けが要る (別機能)
- C: 既存 4 機構を退役させ、隠しかたを infinimation 1 本にする (2026-08-25、ユーザー判断)。
  PC までの実測で infinimation が全項目で優位だったこと、現時点で利用者が居ないことが根拠。
  Quest 未検証のまま Mobile の退避先 (NaNimation) も消しているので、Quest で問題が出た
  場合は git 履歴から戻す

## Next

1. 隠した衣装の PhysBone を止める機能の設計と実装 — blocked-by: none
2. 検出精度の向上 (一覧とビルド結果の乖離) — blocked-by: none
3. Quest 実機での確認 (モバイル GPU の ∞ 頂点)。Mobile 向けの推奨を決めるのに要る
   — blocked-by: none

## Paths

- C: `Docs/research-2026-08-24-next-mechanisms.md` — 新機構探索の正本 (設計案・実測値・AAO 整合条件・未検証リスト)
- C: `Docs/hiding-mechanisms.md` — 既存 4 機構の仕様と根拠 (利用者向け)
- C: `handoff-history.md` — 2026-08-11 の作業メモ (原文)
- C: `../../DevProject` — Unity 検証プロジェクト。新機構プローブは `Assets/_MTLab`、結果は `MTLabOut/`。**検証足場 (`Assets/_MTLab` と `MTLabOut/`) は 2026-08-25 に削除した** (ユーザー指示)。どちらも gitignore 対象で復元できないので、再検証が必要になったら書き直す。E2E は「対象アバターの複製へ `MergeableToggle` を付けて `AvatarProcessor.ProcessAvatar` し、SMR / マテリアルスロット / distinct ボーン / 三角形数を数えて変換なしと比べる」だけの 120 行程度のスクリプト。数値は HANDOFF の verified と `Docs/hiding-mechanisms.md` に転記済み

## Resume protocol

1. `Docs/research-2026-08-24-next-mechanisms.md` を読む (新機構の再開はこれが正本)
2. git fetch と status で work_branch / upstream の drift を実測してから作業する
3. Unity 検証は DevProject が他セッションに使われていないこと (UnityLockfile 不在) を確認してから
