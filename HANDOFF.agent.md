# Agent handoff v1

updated: 2026-08-25
repo: D:/GitHub_WorkSpace/VRC/Packages/com.kie.kie-mergeable-toggle (origin = github.com/Kie610/kie-mergeable-toggle)
work_branch: main
upstream: origin/main (2026-08-25 に 0.5.0-alpha まで push 済み)
base: main@3907539
goal: 手書きのメッシュトグルを AAO が統合できる隠しかたへ機械的に変換する

## State

complete:
- C: 複数トグルで共有する PhysBone の停止 (0.7.0-alpha、2026-08-25)。
  `disableSharedPhysBonesWhenHidden` (既定 ON、公開契約へ追加)。所有トグル集合ごとに
  FX へ `MT_PBStop <n>` レイヤーと `MT_Hidden/<トグルのパス>` ローカル float AAP を生成し、
  全オーナーの AND で m_Enabled を 0 にする。オーナーのクリップが FX 外にもある
  グループは保守側へスキップして警告。E2E 19 検査 PASS + Play 実測 (下の verified)
- C: 隠した衣装の専用 PhysBone を止める機能 (0.6.0-alpha、2026-08-25)。`disablePhysBonesWhenHidden`
  (既定 ON、公開契約へ追加)。`PhysBoneStopper` がアーマチュア側 PB の帰属を保守判定し、
  隠蔽と同じカーブで m_Enabled を落とす。E2E 6 検査 PASS (下の verified)
- C: 0.4.0-alpha 公開済み。一覧を持ち主ごとの折りたたみにした回 (検出・変換のロジックは無変更)
- C: 変換の実装一式 (検出 `ToggleScanner` / 変換 `MergeableTogglePlugin` / 表示 `MergeableToggleInspector`)
- C: 隠しかたを infinimation の 1 本にした (0.5.0-alpha、2026-08-25)。`InfinimationHider` だけを残し、シェイプ(関節)・シェイプ(軸)・UVタイル破棄・NaNimation と `HideMethod` / `MethodOverride` / `MethodAdvisor` / `skipInitiallyHiddenMaterialClone` / `HidePlan.Constant` を削除。インスペクタは機構ポップアップと「自動で割り当てる」を失い、一覧と除外だけになった。**破壊的変更** (利用者は居ないというユーザー判断のもとで実施)。README・Docs/hiding-mechanisms.md・AGENTS.md の公開契約も更新済み
- C: 新機構探索 (2026-08-24)。infinimation (∞デルタのブレンドシェイプ) と微小ウェイト共有 NaN ボーン (d4rk 方式、+1ボーン/トグル) の 2 案が primitive レベルで実測成立。NaN デルタ案とマテリアルスワップ案は不成立が確定。詳細・設計案・AAO 整合条件は `Docs/research-2026-08-24-next-mechanisms.md` (これだけで再開可能な自己完結文書)

verified:
- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTPbCensus.Run; environment=Unity 2022.3.22f1 batchmode/DevProject; scope=PB 帰属の事前実測 (実装前)。保守判定 (チェーンの消費レンダラーが全部トグルで隠れる) で Shinano 10/61 本・MUMUS_all 68/89 本が停止可能、共有ボーン (胸・スカート・尻尾共用) は全件 keep 側。結果は DevProject/MTLabOut/pb_census.txt; counts=passed=2, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTPbE2E.Run; environment=Unity 2022.3.22f1 batchmode/DevProject/AAO Trace and Optimize 付与; scope=Shinano・MUMUS_all を disablePhysBonesWhenHidden OFF/ON で実ビルドし比較。(1) AAO 統合結果 (SMR/MatSlots/Bones/Tris) が OFF と ON で完全一致 (2) 止めた PB (ビルド後 FX の VRCPhysBone.m_Enabled バインディング差分) が独立実装 MTPbCensus の排他集合と末尾ボーン名の多重集合で完全一致 (10/10・68/68、過不足 0) (3) 停止 0 本の空振り無し。結果は DevProject/MTLabOut/pb_e2e.txt; counts=passed=6, failed=0, skipped=0, not-run=0
- C: 2026-08-11 — evidence: status=PASS; kind=runtime; command=NDMF ビルド一式 (当時の E2E スクリプト、詳細は handoff-history.md); environment=Unity 2022.3.22f1 batchmode/DevProject; scope=実アバター 2 体への E2E 変換 (MUMUS_all SMR 21→4・ボーン 453→933 / Shinano SMR 11→2・ボーン 272→469、ポリゴン不変、検出はシーン内 10 体で 80 候補); counts=passed=2, failed=0, skipped=0, not-run=0
- C: 2026-08-24 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -executeMethod MTLabProbe.RunPlay; environment=Unity 2022.3.22f1 Play モード/D3D11/skinWeights=4; scope=Milfy Variant 3 メッシュで ∞シェイプ (w0 無害・w100/50 完全消滅) と εNaN ボーン (scale1 色差分ゼロ・NaN 完全消滅) の描画実測。AAO 込み E2E と実機は対象外; counts=passed=6, failed=0, skipped=0, not-run=0
- C: 2026-08-24 — evidence: status=PASS; kind=build+unit; command=Unity.exe -batchmode -quit (コンパイル) / -executeMethod InfinimationCheck.Run; environment=Unity 2022.3.22f1 batchmode/DevProject; scope=DevProject 全体のコンパイル (error CS 0 件) と、生成したシェイプのデルタ読み戻し (Infinimation=+∞ のまま格納される・既存 BlendShape 経路は有限値のまま)。AAO 込み E2E と実機は対象外; counts=passed=2, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=InfinimationE2E.Run (Unity MCP 経由で実行); environment=Unity 2022.3.22f1 Editor/DevProject/AAO Trace and Optimize 付与; scope=Shinano・MUMUS_all を 変換なし/BlendShape/Infinimation/NaNimation の 4 通りでベイクし統合数を比較。Infinimation は BlendShape と SMR・MatSlots・Bones・Tris が完全一致 (Shinano SMR 11→2・MatSlots 14→6・ボーン 272 不変 / MUMUS_all SMR 21→4・MatSlots 36→18・ボーン 453 不変)。NaNimation は同条件でボーン 272→469・453→933。結果は DevProject/MTLabOut/e2e_infinimation.txt; counts=passed=8, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=Play (Av3Emulator) でユーザー目視 + ベイク結果の機械検査; environment=Unity 2022.3.22f1 Play/DevProject/Shinano Variant (infinimation・AAO 付き); scope=(1) 9 トグルが完全に消える (2) 破綻ポリゴン・ちらつき無し (3) ポーズを付けても残存無し (5) ベイク後の SMR bounds が全件有限・UWO=false・隠蔽シェイプ 9 個が AAO 統合後も生存 (初期非表示 2 件はウェイト 100 で焼かれている); counts=passed=4, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=VRCSDK Build & Test (ユーザーが実施・目視報告); environment=VRChat クライアント PC 版/Shinano Variant (infinimation・AAO 付き); scope=鏡でのトグル往復・初期非表示の入室直後の状態・遠近でのカリング・SDK の Performance ランクの 4 点を確認し、いずれも問題なし。Quest (モバイル GPU) は対象外; counts=passed=4, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=build+runtime; command=Unity MCP 経由で Client.Resolve + RequestScriptCompilation → E2E 再実行 (Editor 占有中のため batchmode は未実施); environment=Unity 2022.3.22f1 Editor/DevProject; scope=1 本化後の再コンパイル (error 0 件。`Library/ScriptAssemblies` の DLL に InfinimationHider が在り MethodAdvisor / HideMethod が消えていることをバイナリ走査で確認) と、E2E の再実行で 1 本化前と同一の数値 (Shinano SMR 11→2・MatSlots 14→6・ボーン 272 / MUMUS_all SMR 21→4・MatSlots 36→18・ボーン 453、変換数 9 と 19); counts=passed=2, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=build; command=Unity.exe -batchmode -quit -projectPath DevProject; environment=Unity 2022.3.22f1 batchmode (Editor を閉じた状態); scope=1 本化後の DevProject 全体のコンパイル。error CS 0 件、exit 0; counts=passed=1, failed=0, skipped=0, not-run=0

- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -executeMethod MTPbPerf.Run; environment=Unity 2022.3.22f1 batchmode Play/DevProject/Av3Emulator (非ローカルクローン無し)/240 フレーム平均; scope=停止対象 PB を直接 enabled=false にして PhysBoneJob を keep/stop 3 回ずつ交互に実測。Shinano 10 本 35 ボーン停止で 2.523→1.874 ms (削減 0.649 ms・25.7%)、MUMUS_all 68 本 358 ボーン停止で 4.662→0.728 ms (削減 3.934 ms・84.4%)。keep のノイズ床は ±0.113 / ±0.085 ms で、いずれも桁違いに有意。wall も 4.76→4.44 / 6.57→5.54 ms と同方向。**全トグルを同時に隠した場合の上限値**。結果は DevProject/MTLabOut/pb_perf.txt; counts=passed=2, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=VRCSDK Build & Test (ユーザーが実施・目視報告); environment=VRChat クライアント PC 版/Shinano Variant; scope=PB 停止の実機動作。単一トグルで表示を切り替えている PB は期待どおり無効化されていた。アウター(Cloth_dress)とスカート(Cloth_skirt)で共有しているスカートボーンは、両方を非表示にしても無効化されなかった (仕様どおり。下記 Decisions の [U6])。再表示時の揺れ直しの見え方については報告なし; counts=passed=1, failed=0, skipped=0, not-run=0

- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTPbE2E.Run; environment=Unity 2022.3.22f1 batchmode/DevProject/AAO 付与; scope=Shinano・MUMUS_all を disableSharedPhysBonesWhenHidden OFF/ON で実ビルドし比較。(1) AAO 統合結果 (SMR/MatSlots/Bones/Tris) が OFF/ON で完全一致 (Shinano SMR=2 MatSlots=5 Bones=272 / MUMUS SMR=4 MatSlots=16 Bones=453。MatSlots が 0.5.0 時点の記録 6/18 より少ないのは数えかたの違いで、原因は特定済み — 下の Decisions を見よ) (2) 停止集合 (カーブ+レイヤー) が独立センサスと末尾ボーン名多重集合で過不足 0 (3) レイヤー数=共有グループ数 (3+α)・AAP 数=オーナー数一致 (4) OFF 側に MT_PBStop / MT_Hidden が生成されない。結果は DevProject/MTLabOut/pb_e2e.txt; counts=passed=19, failed=0, skipped=0, not-run=0
- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -executeMethod MTPbPerf.Run; environment=Unity 2022.3.22f1 batchmode Play/DevProject/240 フレーム平均・3 回交互; scope=ON ビルドの停止対象を直接 disable して回収量を実測 (回収量計測は Animator の書き戻しと競合するため Av3Emulator 無し)。共有回収 (all−excl) は Shinano 0.608 ms (41.8%)・MUMUS_all 0.248 ms (67.8%)、excl のノイズ床 ±0.021/±0.002 で有意。レイヤー代償 (Av3Emulator 有り・全トグル可視での Animators.Update ON−OFF) は Shinano +0.007 ms (ノイズ以下)・MUMUS_all +0.053 ms (有意)。正味でも回収が代償を大きく上回る。Play 中の期待 enabled 状態は全構成で検証 PASS (誤停止 0。可視オーナーを持つ共有 PB は enabled のまま、全オーナー初期非表示のグループはレイヤーで停止)。結果は DevProject/MTLabOut/pb_perf.txt の 17:26 run; counts=passed=32, failed=0, skipped=0, not-run=0

- C: 2026-08-25 — evidence: status=PASS; kind=build+runtime; command=Unity.exe -batchmode -quit -executeMethod MTPbE2E.Run; environment=Unity 2022.3.22f1 batchmode/DevProject/AAO 付与; scope=Codex による全ソースレビュー 2 巡 (1 巡目 8 件・2 巡目 4 件) の指摘をすべて修正したあとの回帰。error CS 0 件、E2E 19 PASS / 0 FAIL、統合結果は修正前と同値 (Shinano SMR=2 MatSlots=5 Bones=272 Tris=137438 / MUMUS_all SMR=4 MatSlots=16 Bones=453 Tris=229953)。スキップ警告がビルドログへ出ることも確認。**修正 5 (隠れる消費者の絞り込み) と修正 6 (初期非表示のコンポーネント無効化) の新しい保守分岐は、この 2 体では踏まれていない**; counts=passed=19, failed=0, skipped=0, not-run=0

- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTGuardLab.Run / MTPbE2E.Run; environment=Unity 2022.3.22f1 batchmode/DevProject; scope=合成アバター `MTGuardLab` で保守分岐を実行検証。(1) 落とせない Renderer を消費者に持つ PB を停止しない (2) 初期非表示のとき AudioSource と PB がシリアライズ時点で enabled=false (3) 同一 GameObject に同型複数は無効化しない (4) m_Enabled が既にアニメーション済みは無効化しない (5) インスペクタのクリーン判定とビルド時の扱いが 4 構成とも矛盾しない (6) スキップ理由がビルドログの警告へ出る。あわせて `MTPbCensus` をパッケージの現行ガードへ独立実装で追随させ、実アバター 2 体の E2E が同じ期待値 (census 28 / 75) で通ることを確認。結果は DevProject/MTLabOut/guard_lab.txt と pb_e2e.txt; counts=passed=34, failed=0, skipped=0, not-run=0

- C: 2026-08-25 — evidence: status=PASS; kind=runtime; command=VRChat クライアント Quest 版でのアップロードと目視 (ユーザーが実施・報告); environment=DevProjectQuest (Unity 2022.3.22f1 / Android) でビルドした Milfy_QuestMobile + 検証用 MT_TestBox; scope=**U4 モバイル GPU での ∞ 頂点の扱い**。トグル対象 (MT_TestBox・SmartPhone) がきちんと消えることを確認。常時表示の Body は無傷。ビルド前の機械検査は 4 項目 PASS (Humanoid / トグル候補 2 件 clean / 両対象に +Infinity 隠蔽シェイプ / 全マテリアル Quest 対応シェーダ)、結果は DevProjectQuest/MTQuestLabOut/milfy_quest.txt。**カリングと Performance ランクは未報告**; counts=passed=1, failed=0, skipped=0, not-run=0

- C: 2026-08-26 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTMergeMatrix.Run; environment=Unity 2022.3.22f1 batchmode/DevProject/AAO 1.9.17; scope=**効きどころの実測**。5 構成 (raw / aao / mt+aao / aao+ms / mt+aao+ms) を 3 体でビルドし SMR 数を比較。MA Mesh Settings の有無を揃えた対 (aao+ms vs mt+aao+ms) での寄与は Shinano 11→2 (-9)、MUMUS_all 21→4 (-17)、Milfy CustomBase 5→5 (**±0**)。寄与はトグルされている SMR の数 (9 / 19 / 6) に対応し、SMR 総数 (15 / 23 / 34) では決まらない。CustomBase で寄与ゼロなのは変換の失敗ではない (統合後メッシュに隠蔽シェイプ blendShapes=1 を確認)。理由は未特定。結果は DevProject/MTLabOut/merge_matrix.txt; counts=passed=15, failed=0, skipped=0, not-run=0

not-run:
- C: 2026-08-26 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTMergeKeys.Run; environment=Unity 2022.3.22f1 batchmode/DevProject; scope=**CustomBase で統合が進まない原因の特定**。AAO が動く直前 (MergeableToggle 有り・TraceAndOptimize 無しで最後まで build) の全 34 SMR について AAO 1.9.17 の CategorizationKey を実測。値が 2 種類以上あるキーは `rootBone` だけで、**変換された 6 個のみ `Armature/Hips`、他 28 個は `Armature/Hips/Hips_Const/Hips`**。bounds・probeAnchor・影・プローブ・UWO・HasNormals・quality・skinnedMotionVectors は一様。分身ボディが MERGE_0 へ入らないのは `RendererAnimationLocations` の差 (ハンドルは material._IsGrayScale がアニメーション、分身ボディは無し)。`Body` は MMD World Compatibility による保護。**シェーダ/マテリアル説は棄却** (AAO のキーに含まれず、APS 10 スロットを衣装マテリアルへ差し替えても SMR は 5 のまま・MatSlots のみ 10→8)。結果は DevProject/MTLabOut/merge_keys.txt と merge_matrix.txt; counts=passed=3, failed=0, skipped=0, not-run=0
- C: 2026-08-26 — evidence: status=PASS(数値は下記); kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTMergeMatrix.Run / MTMergeKeys.Run; environment=Unity 2022.3.22f1 batchmode/DevProject/AAO 1.9.17; scope=**rootBone 正規化見直し (0.8.0-alpha) の検証**。MTMergeKeys で CustomBase の CategorizationKey から rootBone の分割が消えた (2種類以上あるキー: なし)。MTMergeMatrix の aao+ms → mt+aao+ms は Shinano 11→2 (-9)・MUMUS_all 21→4 (-17) を維持、**CustomBase は 5→5 のまま** (残る壁は RendererAnimationLocations。matswap で 5→4)。MTMergeMatrix の exit 1 は Shinano/MUMUS で matswap が差し替え 0 件 not-run になる構成欠落カウントで、退行ではない。結果は DevProject/MTLabOut/merge_matrix.txt・merge_keys.txt (rootbone_*.log); counts=passed=15(数値取得), failed=0, skipped=2(matswap not-run), not-run=0
- U: 再表示時にレスト位置から揺れ直す見え方が許容範囲か (実機の領分。未報告)
- U: 共有 PB 停止の実機 (VRChat クライアント) 確認。Play モードでの機械検証は済み
- U: Quest 実機での遠近カリングと Performance ランク表示。∞ 頂点そのものは 2026-08-25 に確認済み
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
- C: [U1] PB の帰属判定 (2026-08-25、0.6.0-alpha で実装)。stoppable(pb, toggle) ⇔
  「チェーン (rootTransform の子孫 − ignoreTransforms 部分木。root 自身は動かないので
  除外) の消費者 (ウェイト > 0 の SMR + チェーン配下の全 Renderer) が空でなく、
  全部そのトグルの隠蔽対象に含まれる」。誤検出は**止めない側**へ倒す。ウェイト閾値は
  0 (1 頂点でも > 0 なら消費者)。さらに保守ガード 4 つ: 編集時に無効な PB は触らない /
  m_Enabled が既にアニメーションされている PB は触らない (kieApsGate の教訓:
  編集時 enabled は当てにならない。APS のような後から有効化する構成は前者でなく
  こちらで弾ける) / 同一 GameObject に複数 PB があれば触らない (m_Enabled の
  バインディングは (パス,型) で 1 本しか作れず個別に狙えない) / 消費者が空の PB は
  触らない。**VRCPhysBoneCollider は止めない** (参照元 PB が止まればシミュレーション
  コストは発生しないため、独自の共有判定を持つ価値がない)。Contact も対象外。
  根拠: 実装前センサスで Shinano 10/61・MUMUS_all 68/89 本が停止可能、共有ボーンは
  全件 keep (verified 参照)
- C: [U2] 出しかたは公開契約フィールド `disablePhysBonesWhenHidden` (bool、既定 ON) の
  1 つだけ (2026-08-25)。既定 ON の根拠: 判定が保守側で誤停止が構造的に起きにくい /
  CPU 削減はパッケージの存在理由と同方向 / 現時点で利用者ゼロ。トグルごとの指定は
  作らない (必要が出るまで YAGNI。excludedPaths 相当の粒度は契約が重くなる)
- C: [U3] 止めかたは m_Enabled を隠蔽シェイプと同じカーブで 0 にする (2026-08-25)。
  HidePlan へバインディングを足すだけで既存のカーブ書換機構に乗る。AAO への影響は
  E2E で実測し、統合結果 (SMR/MatSlots/Bones/Tris) は OFF/ON で完全一致。
  resetWhenDisabled は触らない (どの値でも揺れ状態は保持されないことが 2026-08-13 に
  実測済みで、再表示はレスト位置からの揺れ直しになる。隠れていた衣装には自然な挙動
  として許容)
- C: MatSlots の基準値 6/18 と現行 E2E の 5/16 の差は、**数えかたの違いだけ**
  (2026-08-25 に静的解析で特定)。0.5.0 時点の InfinimationE2E は
  `MaterialSlots += r.sharedMaterials.Length` を SkinnedMeshRenderer と MeshRenderer
  の両方で回して合算していた。現行の `MTPbE2E` は
  `smrs.Sum(r => r.sharedMaterials.Length)` で SkinnedMeshRenderer だけを数える。
  Shinano・MUMUS_all はどちらも MeshRenderer を 1 個持ち (別計測の `MR=1` で確認)、
  差 1 / 2 はそのスロット数と一致する。プレハブと Variant は当時のままで、統合結果も
  同じ値である。`Docs/hiding-mechanisms.md` の表へ注記済み
- C: [U6] 複数トグル共有 PB の停止は専用レイヤー方式で実装 (0.7.0-alpha、2026-08-25)。
  分類は `PhysBoneStopper.Classify` が全トグル同時に行い、単独所有は従来どおり
  隠蔽カーブへ相乗り、共有所有はオーナー集合ごとにグループ化する。レイヤーは
  グループごとに `MT_PBStop <n>` (1 グループ 1 レイヤー、Active/Stopped の 2 状態、
  WriteDefaults ON、AnyState 不使用、priority int.MaxValue で他プラグインより後ろ)。
  条件は Active→Stopped が全オーナーの `MT_Hidden/<パス>` > 0.5 の AND 1 本、
  Stopped→Active はオーナーごとの < 0.5 を OR (遷移を分ける)。AAP は各トグルの
  隠蔽クリップへ visible=0 / hidden=1 で相乗りし、default はビルド時の activeSelf。
  同期パラメータではないので Expression Parameters を消費しない。保守ガード:
  オーナーの m_IsActive クリップが 1 つでも FX 外 (または未束縛) のグループは
  スキップして警告 (AAP はコントローラをまたげないため)。既存の保守ガード 4 つは
  Classify 内でそのまま全 PB へ適用
- C: [U4] 検出精度は「一覧の正確さ」を追わず「ビルドログでの事後確認」を強化する側で
  確定 (2026-08-25)。一覧の完全化は原理的に不可能 (ビルド時生成ツールのぶんは編集時に
  存在しない) と README に明記済みのため。実装: 変換一覧のログに加えて、止めた PB の
  「トグル -> PB パス」全件をビルドログへ出す。ToggleScanner・インスペクタ一覧は変更しない

- C: [U7] `rootBone` の正規化先は「変換対象以外の SMR の最頻値」へ変更 (0.8.0-alpha、
  2026-08-26)。従来の Humanoid Hips 固定は、アバター内に別の合意 (MA Mesh Settings 等)
  が成立している場合にそれを壊し、CustomBase で統合を割っていた (2026-08-26 特定)。
  選びかた: 変換対象以外の SMR (sharedMesh 有り・rootBone 非 null) の rootBone を
  レンダラー 1 個 1 票の最頻値で採り、同数タイはパスの辞書順、1 つも無ければ従来どおり
  Humanoid Hips → アバタールートへ倒す。localBounds は選んだ rootBone 空間で合併値を
  計算する (従来と同じ関数の引数差し替え)。MA MeshSettingsPluginPass は MA 本体
  シーケンス先頭で走り本パッケージは AfterPlugin(MA) なので、rootBone は本パッケージの
  書き込みが最後 (ソース確認 + CustomBase の観測と整合)。
  **CustomBase の SMR はこれだけでは減らない** (aao+ms 5 → mt+aao+ms 5 のまま)。
  rootBone の分割は解消した (MTMergeKeys で一様を確認) が、変換した APS 6 個は互いに
  統合されて 1 つになるだけで、衣装側の統合グループとは別のまま。マテリアルを衣装側へ
  差し替える matswap 実験では 5→4 になる。**残る壁は `RendererAnimationLocations`**
  (rootBone とは別問題。**対処しないと決定** — 2026-08-26 ユーザー判断)。AAO はアニメーションされる material プロパティの
  名前と既定値まで比較するため、APS の `material._IsGrayScale` と衣装側の lilToon
  プロパティでは値が違い別グループになる。**シェーダの違いそのものはキーではない**
  (マテリアルアニメーションの有無で見ると APS 7 / 衣装 24 / その他 2 が「あり」で
  差にならない)。matswap で 5→4 になるのは、差し替えで `_IsGrayScale` が存在しなくなり
  このキーが変わるため。**追わない理由**: 対処するには他ツールが付けた material
  アニメーションへ手を出すことになり、「迷ったら触らない」という保守方針と正面から
  衝突する。CustomBase はトグルが 6 個しかなく、得られる上限も小さい

## Next

1. 0.7.0-alpha の実機確認 (共有 PB 停止の VRChat クライアント動作) — blocked-by: ユーザー実施
2. Quest 実機での確認 (モバイル GPU の ∞ 頂点) — blocked-by: PC 版の機能充足
   (ユーザー判断で最後に回す)

## Paths

- C: `Docs/research-2026-08-24-next-mechanisms.md` — 新機構探索の正本 (設計案・実測値・AAO 整合条件・未検証リスト)
- C: `Docs/hiding-mechanisms.md` — 既存 4 機構の仕様と根拠 (利用者向け)
- C: `handoff-history.md` — 2026-08-11 の作業メモ (原文)
- C: `../../DevProject` — Unity 検証プロジェクト。新機構プローブは `Assets/_MTLab`、結果は `MTLabOut/`。**検証足場 (`Assets/_MTLab` と `MTLabOut/`) は 2026-08-25 に削除した** (ユーザー指示)。どちらも gitignore 対象で復元できないので、再検証が必要になったら書き直す。E2E は「対象アバターの複製へ `MergeableToggle` を付けて `AvatarProcessor.ProcessAvatar` し、SMR / マテリアルスロット / distinct ボーン / 三角形数を数えて変換なしと比べる」だけの 120 行程度のスクリプト。数値は HANDOFF の verified と `Docs/hiding-mechanisms.md` に転記済み。
  **PB 停止用の足場は 2026-08-25 に書き直した**: `Assets/_MTLab/Editor/MTPbCensus.cs`
  (帰属センサス + E2E 照合用の独立実装) と `MTPbE2E.cs` (OFF/ON ベイク比較)。
  引き続き gitignore 対象なので、消えたら HANDOFF の判定仕様から再構築する。
  **CPU 削減量の計測足場は 2026-08-25 に追加**: `Assets/_MTLab/Editor/MTPbPerf.cs`
  (`_tools/perf-harness/PerfCompare.cs` が雛形。ON ビルドの停止対象を直接
  `enabled=false` にして none/excl/all を交互に測り、レイヤー代償は可視状態の
  OFF/ON で Animators.Update を比べる。Sampler は `Assets/_PerfProbeRuntime/Sampler.cs`、
  Play 中の enabled 検証は同 `MTPbPerfParameterDriver.cs`)。結果は `MTLabOut/pb_perf.txt`。
  再構築時の注意 3 つ: (1) census のパスは AAO MergeBone の `$` 改名でビルド後に
  引けないので、変換前に PB 参照へ解決して生死で追跡する (2) 回収量計測は
  Av3Emulator を載せない (FX の m_Enabled 書き戻しと直接 disable が競合する)
  (3) emulator の Mirror/Shadow クローンは VRCPhysBone を破棄した複製なので、
  FindObjectsOfType でドライバを拾うときはクローンを除外する

## Resume protocol

1. `Docs/research-2026-08-24-next-mechanisms.md` を読む (新機構の再開はこれが正本)
2. git fetch と status で work_branch / upstream の drift を実測してから作業する
3. Unity 検証は DevProject が他セッションに使われていないこと (UnityLockfile 不在) を確認してから
