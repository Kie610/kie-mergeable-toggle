# Agent handoff v1

updated: 2026-09-03
repo: D:/GitHub_WorkSpace/VRC/Packages/com.kie.kie-mergeable-toggle (origin = github.com/Kie610/kie-mergeable-toggle)
work_branch: main
upstream: origin/main = 6c3f1fc / package.json は 0.8.0-alpha (2026-08-30 に git fetch で実測)。
  ローカルが 14 コミット先行しており、0.8.1-alpha〜0.8.3-alpha は未 push。
  0.9.0-alpha (emptyHiddenMaterialSlots) は作業ツリー上で未コミット (2026-09-03。ユーザー承認待ち)
base: main@3907539
goal: 手書きのメッシュトグルを AAO が統合できる隠しかたへ機械的に変換する

## State

complete:
- C: 隠れたマテリアルスロットの差し替え (0.9.0-alpha、2026-09-03)。`emptyHiddenMaterialSlots`
  (既定 ON、公開契約へ追加)。AAO の統合後 (`HiddenSlotPass`、Optimizing の AAO 後) に、頂点が全部
  隠蔽シェイプで覆われるスロットを同梱シェーダの `MT_Empty` へ差し替える。所有トグルが 1 つならその
  クリップへ PPtr カーブ、複数なら `MT_SlotOff <n>` レイヤー (AAP の AND ゲート、`AndGateLayer` で
  `MT_PBStop` と共通化)。`MT_Hidden/<パス>` AAP は FX に作れる全変換トグルへ作る。Android では生成しない。
  静的 E2E 54 PASS + Play 64 PASS (下の verified)。経緯と実測は `Docs/hidden-cost-revisit-2026-09-02.md`
  (D1 の開き直し → 1a 却下 → P2a 採用 → 実装)。**コミットはユーザー承認待ち**
- C: 隠している間のコストの再検討 (2026-09-02)。ユーザーの明示指示で D1 を開き直し、案を 0 から
  列挙し直した。途中で実装した 1a (初期非表示トグルのゲート付き別レンダラー化) はユーザー判断で却下し
  実装を取り消した (差分は `DevProject/MTLabOut/rejected_1a_separateInitiallyHiddenToggles.patch`)
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
- C: 2026-09-03 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -quit -projectPath DevProject -executeMethod MTSlotSwapE2E.Run; environment=Unity 2022.3.22f1 batchmode/DevProject/AAO 1.9.17; scope=**隠れたスロット差し替えの静的検査**。Shinano_TEST / MUMUS_all / Milfy_CustomBase を emptyHiddenMaterialSlots OFF/ON で実ビルドし、(1) SMR・スロット・三角形数が OFF/ON で同じ (2) OFF に MT_SlotOff / MT_Empty が出ない (3) 独立計算した期待スロット (Shinano 1・MUMUS 11・Milfy 3) だけに PPtr カーブ / ゲートがあり、単独所有はクリップのキーと 1:1、共有は Active=元 / Stopped=Empty で条件が全オーナーの MT_Hidden (4) 初期マテリアルが「全オーナー初期非表示」のときだけ MT_Empty (5) MT_Empty が同梱シェーダでアセット保存済み。結果 DevProject/MTLabOut/slot_swap_e2e.txt; counts=passed=54, failed=0, skipped=0, not-run=0
- C: 2026-09-03 — evidence: status=PASS (検査側の限界による FAIL 2 を含む); kind=runtime; command=Unity.exe -projectPath DevProject -executeMethod MTSlotSwapPlay.Run (GUI、MTLabOut/run_slot_play.ps1 で無人実行); environment=Unity 2022.3.22f1 GUI Play/DevProject; scope=**差し替えの実行検査**。ビルド済み FX を Animator に載せ、トグルレイヤーの遷移を外してステートを固定し、スロットごとに「全オーナーを隠す → Empty」「ゲートが Stopped」「1 つ戻す → 元」「また隠す → Empty」を読む。Shinano_TEST / MUMUS_all × AAO アニメーター最適化 ON/OFF。MUMUS OFF で 10/11 スロット・ON で 2/11 が検査可能で、可能なもの全部が期待どおり。FAIL 2 は MUMUS OFF の 03_Cos_casual (6 オーナー) の「1 つ戻す」で、検査側が選んだステートでは隠蔽シェイプ自体が 0 に戻らなかった (製品の差し替えはシェイプに追従しており正しい)。not-run 12 は片方向トグル (Shinano) と BlendTree 化 (MUMUS 最適化 ON) で駆動できないもの。結果 DevProject/MTLabOut/slot_swap_play.txt; counts=passed=64, failed=2, skipped=0, not-run=12
- C: 2026-09-03 — evidence: status=PASS; kind=runtime; command=MTLabOut/run_slot_play.ps1 (時刻付き進捗ログ MTLabOut/run_slot_play_status.log); environment=Unity 2022.3.22f1 GUI/DevProject; scope=**無人実行が止まる原因の特定**。GUI Unity の無人実行が「ウィンドウをアクティブにしたときだけ進む」(ユーザー観察。起動から検証開始まで 16.5 分、Play 中 4 分停止)。原因は Preferences の Interaction Mode = Default (非アクティブ時に更新を間引く)。検査の入口で EditorPrefs "InteractionMode"=1 (No Throttling) にすると、起動から完了まで 75 秒で完走 (ウィンドウ操作なし)。ビルド時間 (差し替え ON/OFF): Shinano 4.4/2.9 s、MUMUS_all 3.7/3.1 s。差し替えパス自体は batchmode ログで 17〜48 ms、Milfy 1.6 s (Milfy の 1 ビルド約 3 分の大半は VirtualLens2 92 s・APS 20 s・AAO AnimOpt 17 s); counts=passed=1, failed=0, skipped=0, not-run=0
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

- C: 2026-08-28 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTMergeGate.Run; environment=Unity 2022.3.22f1 batchmode/DevProject/AAO 1.9.17; scope=**MUMUS_all が SMR 4 で止まる原因の特定**。AAO 自身の `AutoMergeSkinnedMesh.FilterMergeMeshes` / `CategoryMeshesForMerge` を NDMF パスから実物のまま呼び、3 体を「変換なし / MergeableToggle あり」の 2 構成で採取。**残る 4 個は `Body` (MMD World Compatibility 除外) / `Body_body` (`IsAnimatedForbidden` ← `object:m_Materials.Array.data[0]`) / `Body_hand` (同) / 統合結果 1 個**。変換ありでは足切り通過 18 個の CategorizationKey が 1 種類だけ (分割ゼロ)、変換なしでは同じ 18 個が全部単独グループで統合ゼロ。**19→1 は完全に本パッケージの寄与で、やり残しは無い**。予測 6 に対し実測 4 の差 2 は `Cos_EarRings` / `Cos_HairPin` で、足切り後に AAO の未使用オブジェクト削除で丸ごと消える (統合ではないので予測式の外)。Shinano は予測 2 = 実測 2、CustomBase は予測 7 / 実測 5 (同じく 2 個が削除)。結果は DevProject/MTLabOut/merge_gate.txt; counts=passed=6, failed=0, skipped=0, not-run=0

- C: 2026-08-28 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTMergeGate.Run; environment=Unity 2022.3.22f1 batchmode/DevProject/AAO 1.9.17; scope=**マテリアル差し替えを持つメッシュの扱い**。全 SMR のアニメーションされているプロパティを採取。PPtr 差し替え (`m_Materials.Array.data[N]`) を持つのは Shinano `Body` / MUMUS_all `Body_body`・`Body_hand` / CustomBase `Body_base` の 4 個で、**3 体を通して自動統合を通った例はゼロ** (それぞれ MMD 除外 / IsAnimatedForbidden / マルチパス+IsAnimatedForbidden で落ちる)。CustomBase `Body_base` は lilToon の `material._LightMinLimit` 等 29 個の float マテリアルプロパティも動かしており、AAO はこちらを許可する; counts=passed=3, failed=0, skipped=0, not-run=0

- C: 2026-08-28 — evidence: status=PASS; kind=runtime; command=VRChat クライアント PC 版・PCVR でのアップロードと目視 (ユーザーが実施・報告); environment=Shinano_TEST (Shinano Variant + MergeableToggle + TraceAndOptimize); scope=**∞ デルタで自分視点が描画されない不具合の切り分け**。①素のアバター=正常 / ②TraceAndOptimize 単独=正常 / ③MergeableToggle 単独=**変換対象だけ消える**。鏡 (ミラークローン) は全構成で正常。隠蔽シェイプのウェイトを全部 0 (全表示) にしても消えたまま。ケモミミが視点位置によって出没。Desktop・PCVR とも同じ。Av3Emulator では再現しない; counts=passed=3, failed=0, skipped=0, not-run=0
- C: 2026-08-28 — evidence: status=PASS; kind=runtime; command=VRChat クライアントでのアップロードと目視 + Unity.exe -batchmode -quit -executeMethod MTBuiltDump.Run; environment=同上 / Unity 2022.3.22f1 batchmode; scope=**修正の確認 (0.8.1-alpha)**。デルタを `1e6` へ変えたビルドで自分視点に表示されることを実機で確認。隠すべき衣装は消えており、視界に異物は出ない。手元の実測ではスキニング後の飛び先が距離 1,732,050 (=1e6×√3)、**散らばりは最大 8m** (Cloth_sweater) で巨大な三角形にはならない。角径はサブピクセルかつ遠クリップ面の外; counts=passed=2, failed=0, skipped=0, not-run=0
- C: 2026-08-28 — evidence: status=PASS; kind=runtime; command=VRChat クライアント PC 版 (ユーザーが実施・報告); environment=Shinano_TEST; scope=**0.7.0-alpha の共有 PB 停止の実機確認**。共有している PB は、所有トグルが全部非表示になったときに無効化されていた; counts=passed=1, failed=0, skipped=0, not-run=0
- C: 2026-08-29 — evidence: status=PASS; kind=runtime; command=Unity.exe -projectPath DevProject -executeMethod MTVertexPerf.RunPass0 / .RunPass1 (GUI、batchmode 不可); environment=Unity 2022.3.22f1 GUI/DevProject/VRCSDK 3.10.4/AAO 1.9.17/MA 1.18.2、Shinano_TEST を 10 体・240 フレーム × **8 反復**、warmup 60、追加描画 8 回/フレーム、Av3Emulator 無し; scope=**隠している間も払う頂点段のコストと変換の収支**。A と B を同一反復の中で隣接して測り、反復ごとの d = B - A で判定 (|平均 d| > d の半レンジ かつ符号が全反復で揃う)。**有意差あり。全 6 条件 (2 skinning × 3 状態) で符号が揃った**。project-default のフレーム時間 (10 体・追加描画込み) は 全表示 d=-1.235 ms (noise 0.319、B が速い) / 半分隠す +2.503 (0.345) / 全隠し +3.353 (0.201)。CPU メインスレッドは -1.236 / -0.601 / +0.595 ms、MeshSkinning.GPUSkinning は -0.303 / -0.114 / +0.127 ms (すべて有意)。cpu-skinning 周回の `MeshSkinning.Skin` (フレーム 1 回・非増幅) は +11.07 / +26.77 / +34.41 ms、**隠していても払う頂点段は 1 体あたり約 3.4 ms/frame**。draw calls / batches は 4623→1743 / 3183→1743 / 1383→1743 で分散ゼロ。ジオメトリは 1 体あたり SMR 11→2、サブメッシュ 13→5、**マテリアルスロット 13→5** (異なるマテリアル数 5 まで束ねられる。同じマテリアルを別 SMR で持つ衣装のスロットが畳まれる)、頂点 86,203 は不変。**収支の分岐点はトグルの約 17%** (全表示と半分隠すの線形内挿)。画角の証拠は計測カメラの 2 枚レンダリング差分で全 96 条件フラスタム内 10/10 体・塗り面積 0.6〜1.0%。結果は DevProject/MTLabOut/vertex_perf_run6_final.txt (生値 vertex_perf_raw_run6_final.tsv); counts=passed=96, failed=0, skipped=0, not-run=0
- C: 2026-09-02 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTHiddenGroupE2E.Run; environment=Unity 2022.3.22f1 batchmode/DevProject/AAO 1.9.17; scope=**separateInitiallyHiddenToggles (0.9.0-alpha) の E2E**。Shinano / MUMUS_all / CustomBase を OFF / ON / ALL で実ビルドし、初期非表示トグルが素体とは別の 1 SMR へ統合されること (Shinano SMR 2→3・MUMUS_all 4→5)、統合先 GameObject が activeSelf=false で `MT_HiddenGroup 1` レイヤーが GameObject の m_IsActive を駆動すること (AAO は activeness プロパティが 1 本だと GameObject 側へ写像する)、統合できないメンバー (MUMUS `Body_hand`) は SMR の m_Enabled で残ること、メンバーの隠蔽シェイプが統合先にだけ過不足なくあること、三角形数不変を検査。CustomBase はセンサスがビルド時生成トグルを見られないので not-run (生成物は整合)。Shinano の ALL は「隠すクリップしか持たないトグルを初期非表示にすると AAO が隠蔽シェイプを正しく凍結する」ため両方向クリップ持ちに限定 (製品の機構は元から初期非表示のトグルだけを対象にするので踏まない)。結果 DevProject/MTLabOut/hidden_group_e2e.txt; counts=passed=49, failed=0, skipped=0, not-run=2 (17:25 の再実行。CustomBase の 2 件は not-run)
- C: 2026-09-02 — evidence: status=PASS; kind=runtime; command=Unity.exe -projectPath DevProject -executeMethod MTVertexPerf.RunG0 / RunG1 (GUI、無人連鎖 run_g_chain.ps1); environment=Unity 2022.3.22f1 GUI/DevProject、MUMUS_all Variant を 10 体・追加描画 8 回・near 構図 (塗り面積 14.0〜30.1%)・8 反復・対の差; scope=**初期非表示グループのゲートの効き目 (系列 G)**。G0 (ゲート無し) の d=B−A は 全表示 wall −4.958 / メインスレッド −2.667、半分隠す +4.989 / +0.870、**全隠し +8.042 / +4.087** (すべて有意)。G1 (19 トグル全部を初期非表示にしてゲート付きグループへ) は 全表示 −5.280 / −2.704、半分隠す +5.319 / +0.928 (G0 と同じ)、**全隠し +0.015 / −0.029 で判定不能 (A と区別が付かない。draw calls・可視 SMR も一致)**。MUMUS_all は素体側と統合できる常時表示メッシュが無いので SMR は G0/G1 とも 4 で増えない。最初の G1 はハーネスのゲート模倣が GameObject の active を戻しておらず無効 (退避済み)。結果 DevProject/MTLabOut/vertex_perf_G0.txt / vertex_perf_G1.txt (生値 _raw.tsv); counts=passed=96, failed=0, skipped=0, not-run=0
- C: 2026-09-02 — evidence: status=PASS; kind=runtime; command=Unity.exe -projectPath DevProject -executeMethod MTVertexPerf.RunW1 / RunW2 (GUI、無人連鎖); environment=Unity 2022.3.22f1 GUI/DevProject、Shinano_TEST と MUMUS_all を 10 体・追加描画 8 回・near 構図・8 反復、**R (未変換・AAO 無し) / A (AAO のみ) / B (現行変換) / S (B + P2a) の 4 構成を同じ反復で交互に測定**; scope=ユーザー要望の 4 構成比較。表示中は S=B (全差が判定不能)。隠している間は S が B の損を消す: MUMUS_all 半分隠す wall 14.46 → 11.69 (S−B −2.77 有意)・メインスレッド 7.79 → 6.52 (−1.27 有意)、全隠し wall 13.33 → 7.30 (−6.04 有意)・メインスレッド 7.85 → 4.42 (−3.43 有意)、A は 6.95 / 3.79。Shinano は全隠しだけ wall 10.08 → 7.56 (−2.53 有意)。A−R は小さい (Shinano draw call −960、MUMUS 同数)。結果 DevProject/MTLabOut/vertex_perf_W1.txt / vertex_perf_W2.txt; counts=passed=192, failed=0, skipped=0, not-run=0
- C: 2026-09-02 — evidence: status=PASS; kind=runtime; command=Unity.exe -projectPath DevProject -executeMethod MTVertexPerf.RunS0 / RunS1 / RunS2 / RunS3 / RunS4 / RunG0 (GUI、無人連鎖 run_g_chain.ps1); environment=Unity 2022.3.22f1 GUI/DevProject、Shinano_TEST と MUMUS_all Variant を 10 体・追加描画 8 回・near 構図・8 反復・対の差、差し替えはハーネスが sharedMaterials を直接書き換えて PPtr カーブを模倣; scope=**案 P2 (隠れたマテリアルスロットの差し替え) の効き目**。P2a (統合スロットのまま差し替え): MUMUS_all で 半分隠す wall +2.969 → **+0.642 (判定不能)**・メインスレッド +0.849 → **−0.578**、全隠し wall +6.190 → **+0.343 (判定不能)**・メインスレッド +3.885 → **+0.600**、全表示は不変 (draw calls 4264 で同数)。Shinano は衣装 7 点が 1 マテリアルなので全隠しだけ改善 (wall +2.734 → +0.211 判定不能)。P2b (トグルごとにマテリアル複製): 全表示の draw call が変換なしと同数になり メインスレッド Shinano +1.801・MUMUS +3.677 と純損、スロット数も統合前と同じ → 却下。結果 DevProject/MTLabOut/vertex_perf_S{0..4}.txt / vertex_perf_G0.txt; counts=passed=288, failed=0, skipped=0, not-run=0
- C: 2026-09-02 — evidence: status=PASS; kind=build; command=Unity.exe -batchmode -quit -projectPath <MA 抜きの使い捨てプロジェクト> -logFile <log>; environment=Unity 2022.3.22f1、DevProjectMini の manifest から **Modular Avatar だけを除いた**最小プロジェクト (VRCSDK Avatars/Base + NDMF + 本パッケージ。実体は DevProject/Packages を file: 参照); scope=**Modular Avatar が無い環境で本パッケージが成立するかの確認**。Editor の asmdef は `nadena.dev.modular-avatar.core` を無条件に参照しているため、MA 未導入だとアセンブリごとコンパイルされない懸念があった (README と配布文書は MA を「任意」と書いており、`vpmDependencies` にも MA は無い)。**結果は問題なし**: error CS 0 件で、`com.kie.kie-mergeable-toggle.Editor.dll` と `.Runtime.dll` の両方が生成された。Unity は解決できない asmdef 参照を黙って落とし、`MT_MA_PRESENT` が未定義になるので MA 依存のコード (`MenuLabelResolver` のメニュー名解決と `ToggleScanner` の Merge Animator 追跡) だけが外れる。**MA は文書どおり任意で正しい**。ログは使い捨てプロジェクトのため残っていない (再現手順は上記コマンド); counts=passed=1, failed=0, skipped=0, not-run=0
- C: 2026-08-29 — evidence: status=PASS; kind=runtime; command=Unity.exe -projectPath DevProject -executeMethod MTSlotPerf.Run (GUI); environment=Unity 2022.3.22f1 GUI/DevProject、合成メッシュ (頂点 6,321 / 三角形 12,288 / ボーン 1 / 全スロット同一マテリアル)、10 体・240 フレーム × 8 反復・追加描画 32 回/フレーム; scope=**マテリアルスロット単価の実測 (目標 A)**。頂点数・三角形数・ボーン・マテリアルを固定し スロット数だけ 1/2/4/8/16 と変えた。判定は反復ごとの対の差。**スロット 1 個あたり CPU メインスレッド 0.172 ms、レンダースレッド 0.198 ms** (10 体・33 描画あたり。N=4〜16 で傾きが一定)。1 体・1 描画あたりに直すと 0.52 / 0.60 µs。SetPass Calls は ±0 (同一マテリアルのため)、draw calls は 990/スロットで分散ゼロ。wall frame time は N=16 でのみ有意 (0.056 ms/スロット) — CPU 増分がフレーム下限 約 7 ms に隠れるため。**本拡張のスロット削減 13→5 が説明するのは、run6 で観測した全表示時のメインスレッド短縮 (1 描画あたり 0.137 ms/10 体) のうち約 31% で、残り約 7 割は SMR 11→2 のレンダラー個数削減に由来する**。結果は DevProject/MTLabOut/slot_perf_run2_final.txt (生値 slot_perf_raw_run2_final.tsv); counts=passed=40, failed=0, skipped=0, not-run=0
- C: 2026-08-29 — evidence: status=PASS(方法論); kind=runtime; command=同上を 3 回 (03:07 / 04:06 / 14:01); environment=同上; scope=**判定方法の欠陥と修正**。反復 3 回で「A の平均と B の平均の差」を見る方式だと、ラン間の機体状態のドリフト (同条件で wall 12.19 ms と 8.75 ms) が差へ混ざり、全表示の結論が run1 有意 / run2 判定不能 と割れた。A/B は同一反復で隣接して測っているので、**反復ごとの対の差を集める方式へ変えたところ 8 反復で全条件の符号が揃った**。以後の負荷比較はすべて対の差で判定する; counts=passed=3, failed=0, skipped=0, not-run=0

not-run:
- C: 2026-08-26 — evidence: status=PASS; kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTMergeKeys.Run; environment=Unity 2022.3.22f1 batchmode/DevProject; scope=**CustomBase で統合が進まない原因の特定**。AAO が動く直前 (MergeableToggle 有り・TraceAndOptimize 無しで最後まで build) の全 34 SMR について AAO 1.9.17 の CategorizationKey を実測。値が 2 種類以上あるキーは `rootBone` だけで、**変換された 6 個のみ `Armature/Hips`、他 28 個は `Armature/Hips/Hips_Const/Hips`**。bounds・probeAnchor・影・プローブ・UWO・HasNormals・quality・skinnedMotionVectors は一様。分身ボディが MERGE_0 へ入らないのは `RendererAnimationLocations` の差 (ハンドルは material._IsGrayScale がアニメーション、分身ボディは無し)。`Body` は MMD World Compatibility による保護。**シェーダ/マテリアル説は棄却** (AAO のキーに含まれず、APS 10 スロットを衣装マテリアルへ差し替えても SMR は 5 のまま・MatSlots のみ 10→8)。結果は DevProject/MTLabOut/merge_keys.txt と merge_matrix.txt; counts=passed=3, failed=0, skipped=0, not-run=0
- C: 2026-08-26 — evidence: status=PASS(数値は下記); kind=runtime; command=Unity.exe -batchmode -quit -executeMethod MTMergeMatrix.Run / MTMergeKeys.Run; environment=Unity 2022.3.22f1 batchmode/DevProject/AAO 1.9.17; scope=**rootBone 正規化見直し (0.8.0-alpha) の検証**。MTMergeKeys で CustomBase の CategorizationKey から rootBone の分割が消えた (2種類以上あるキー: なし)。MTMergeMatrix の aao+ms → mt+aao+ms は Shinano 11→2 (-9)・MUMUS_all 21→4 (-17) を維持、**CustomBase は 5→5 のまま** (残る壁は RendererAnimationLocations。matswap で 5→4)。MTMergeMatrix の exit 1 は Shinano/MUMUS で matswap が差し替え 0 件 not-run になる構成欠落カウントで、退行ではない。結果は DevProject/MTLabOut/merge_matrix.txt・merge_keys.txt (rootbone_*.log); counts=passed=15(数値取得), failed=0, skipped=2(matswap not-run), not-run=0
- U: 再表示時にレスト位置から揺れ直す見え方が許容範囲か (実機の領分。未報告)
- U: ∞ で描画されなくなるクライアント側の機序。有限値で回避できたので追っていない
- C: 収支の分岐点は 2026-08-29 の 8 反復・対の差で **約 17%** と実測 (上の verified)。エディタの構図での値であり、実機での再確認は残る
- U: 実機 (VRChat クライアント) での再現確認。エディタ計測は 10 体を小さく並べた構図 (塗り面積 1%) なので、ピクセル段が支配的な実際の見えかたでは収支が変わり得る
- U: 今後の負荷比較。**A は実測完了 (上の verified)**。残りは B = SMR の負荷要因の分解、C = lilToon の設定項目ごとの負荷、D = PhysBone の負荷要因。**いずれも実測は未着手**。計画は `Docs/perf-research-backlog.md`、C と D の机上調査は 2026-08-29 に Codex へ委任して `Docs/perf-research-liltoon.md` と `Docs/perf-research-physbone.md` に結果がある (Claude 側は未検証)
- U: Quest 実機での遠近カリングと Performance ランク表示。∞ 頂点そのものは 2026-08-25 に確認済み
- U: U5 εNaN ボーン方式は未着手のまま棚上げ (infinimation で足りたため。研究文書に設計案は残っている)

## Decisions

- C: MA Mesh Cutter を生成して MA に処理させる案 (方針B) は不成立 (2026-08-11、詳細は履歴)
- C: Modular Avatar への依存は持たない (0.1.0-alpha で除去)
- C: 変換後は rootBone→Hips / localBounds→合併値 / UWO→false へ正規化する
- C: infinimation のデルタは NaN 不可 (Unity が格納時に 0 に潰す。実測)。∞ も不可 (自分視点で描画されなくなる。2026-08-28 実機)。有限の遠方値 `1e6` で作る
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

- C: [U9] 隠蔽シェイプのデルタは**有限の遠方値 `1e6`** で作る (0.8.1-alpha、2026-08-28)。
  `+Infinity` は VRChat クライアントの自分視点でレンダラーごと描画されなくなる (実機で特定)。
  ボーン方式 (NaNimation 系) は**採用しない** — 過去に試して限界と使いにくさが分かっている
  というユーザー判断。有限値でも軽量化の実質 (ピクセル段の削減) は失われない。∞ が余分に
  持っていたのは「bounds を再計算されたら丸ごとカリングされる」性質だけで、それが不具合の
  正体だった。距離は遠クリップ面の外かつ角径サブピクセルであれば足り、`1e6` は実測で
  スキニング後の散らばりが最大 8m と小さく、巨大な三角形にならない
- C: [U8] マテリアル差し替え (PPtr) を持つメッシュの統合は**追わない** (2026-08-28)。
  AAO の自動統合は `IsAnimatedForbidden` で object/PPtr アニメーション
  (`m_Materials.Array.data[N]`) を一律禁止する。float は `m_Enabled` / `blendShape.*` /
  `material.*` だけ許可 (`material.*` は `GetDefaultValue` で既定値を解決でき、統合後の
  カーブへ移し替えられる)。**この禁止は安全側へ倒すための保守判断**である。手動の
  Merge Skinned Mesh は差し替えに対応済み (`MergeSkinnedMeshProcessor` がスロット番号を
  張り替える。CHANGELOG #274) だが、統合でマテリアルスロットが共有されると片方だけの
  差し替えが同居メッシュを巻き込むため、手動側は `material-animation-differently` 警告を
  出して**ユーザーに採否を委ねる**。自動統合には判断を仰ぐ余地が無いので一律禁止に
  している (事故履歴: CHANGELOG 1.9.3 #1650 "Auto Merge Material Slots may break material
  swapping animations")。**本パッケージの守備範囲外** — 差し替えは素体・衣装側の
  ギミックであり、剥がせば見た目が壊れる

- C: [U10] 隠している間の統合メッシュのコストへ**機構では手を打たない** (2026-08-30)。
  手を打つのは「どのトグルを変換するか」の選定で、道具は既存の `excludedPaths` を使う
  (新規実装ゼロ)。**公開契約は変わらない (破壊的変更ではない)**。根拠と却下した案の一覧は
  `Docs/hidden-cost-decision.md` (自己完結)。要点だけ引くと:
  (1) 全隠しの +34.409 ms (CPU スキニング周回・10 体) は
  「隠した頂点のスキニング 15.550 (45.2%) / 統合そのもののペナルティ 11.068 (32.2%) /
  infinimation のシェイプ評価 7.791 (22.6%)」へ誤差なく 3 分解でき、どれも機構では削れない。
  (2) **VRChat PC の GPU スキニング経路では、全隠しのコストの主因はスキニングではない** —
  スキニング段の差は +0.127 ms でフレーム時間差 +3.353 ms の 3.8% しか説明せず、
  残りは描画コマンドと頂点シェーディング。止めるべきは skin でなく draw で、それは
  レンダラー無効化＝統合の放棄に戻る。しかも同経路の全表示では**統合したほうが安い**
  (`MeshSkinning.GPUSkinning` A 0.488 → B 0.185 ms) ので、成分 (2) は PC に存在しない。
  (3) AAO は activeness がアニメーションされたメッシュも統合する
  (`MergeAnimatingSkinnedMesh`、`CategorizationKey` に `Activeness` と
  `ActivenessAnimationLocations` を持つ)。**「粒度を分ける」機構を本パッケージが作る必要は無い。**
  ただし束ねる条件は AnimationLocation 集合の完全一致で、実測では Shinano 9 件・
  MUMUS_all 18 件が**全部 ORPHAN** (`MTLabOut/merge_gate.txt`)。1 トグル 1 メッシュの構成では
  発火しないので、除外は原則「そのメッシュはレンダラー 1 個として残る」を意味する。
  (4) 収支を決める変数は**トグル数ではなく隠した頂点数**。「トグルの約 17%」は誤り
  (半分隠す条件はトグル数 50% でも頂点数 78% を隠していた)。正しくは隠せる頂点の約 26%、
  1 体あたり 11,575 頂点。1 トグル単位では、表示されている時間の割合 p に対して
  `頂点数 < p/(1-p) × 12,900` なら変換して得。
  (5) **この係数は計測構図に強く依存する** — 描画 1 回/frame へ引き直すと分岐点は
  26% → 54.5% へ動く (導出値)。機構へ投資する前に構図を実機側へ寄せて測り直す (目標 E)。
  既定は全変換のまま変えない (p を示すビルド時の信号が無いため)。インスペクタへ
  トグルごとの頂点数を出す方向 (D5) は採るが、実装は目標 E の後
- C: [U11] 目標 E を実測し、[U10] の D1〜D5 を**確定**した (2026-08-30)。
  塗り面積 × 追加描画回数の 2×2 (run6 / E1 / E2 / E3、各 48/48 行・visibility failure 0)。
  結果は `Docs/perf-research-backlog.md` §6 の「結果」、生データは
  `DevProject/MTLabOut/vertex_perf_E1.txt` / `_E2.txt` / `_E3.txt`。
  (1) **CPU メインスレッドの分岐点は 4 構成すべて約 89%** (89.2/89.2/89.1/89.8)。
  構図に鈍感な基準線。Shinano_TEST では 1 体あたり約 4 万頂点 (隠せる頂点 44,764 の 89%)
  で、9 トグルのうち 8 個ぶんを隠してようやく釣り合う。**利用者へ出すのは比率のほう** —
  絶対値はトグル数と頂点数の機体条件に依存するので一般化しない。
  (2) 追加描画を実機並みの 1 回にすると、wall / GPU / CPU frame time は 3 状態とも
  約 7 ms の床に張り付いて**全条件で判定不能**になる (A も B も 6.9〜7.1 ms)。
  床の影響を受けない CPU メインスレッドで見た全隠しのペナルティは **0.011 ms/体**。
  (3) 塗り面積を 1% → 18% にすると wall の分岐点は **25.9% → 58.0%** へ動く。
  「ピクセル段は A/B に同じだけ乗るので相殺される」という当初の見込みは**誤り**で、
  GPU がピクセルバウンドへ近づくと B の余分な頂点シェーディングが吸収され、
  同時に描画コマンドが増えるほど B の draw call 削減の得が大きくなる (両方 A/B 非対称)。
  (4) **判定基準 (wall の分岐点が 45% 以上) を満たした → 新機構は不要で確定。**
  25.9% は「塗り面積 1%・追加描画 8 回」の 1 点でだけ現れる値なので**利用者向けに使わない**。
  D5 (インスペクタへトグルごとの頂点数) の実装ブロックは解除。
  ハーネスは `DevProject/Assets/_MTLab/Editor/MTVertexPerf.cs` の
  `Tools/MTLab/Vertex perf E1 / E2 / E3` (gitignore 対象。near 構図は間隔 0.35m・マージン 1.02)

- C: [U12] 目標 F を実測した (2026-08-30)。**除外の効き目は限定的**という結論。
  結果は `Docs/perf-research-backlog.md` §7、生データは
  `DevProject/MTLabOut/vertex_perf_F_k{0,3,6,9}.txt`。4 本とも 48/48 行・
  visibility failure 0・塗り面積 15.8〜20.4% (構図は E3 と同じ)。
  (1) **帰無対照 k=9 は PASS** — 全件除外で B は A と同じ構成 (SMR 11 / スロット 13 が一致) になり、
  全 6 条件が判定不能、メインスレッドの d は -0.003 / -0.072 / +0.024 だった。
  (2) **除外しても CPU メインスレッドの「隠している間の損」は減らない** —
  全隠しの d が k=0/3/6 で +0.496 / +0.484 / +0.547 と横ばい (noise 0.321/0.459/0.226 で
  互いに区別できない)。変換したまま残る頂点は 44,764 → 13,832 → 3,046 と 93% 減らしたのに動かない。
  **この損を生んでいるのは、統合メッシュが常に持つレンダラーとマテリアルスロットである**
  (全隠しでは A の生存レンダラー 11-9=2 と B(k=6) の 8-6=2 が同数になり、差はスロット 1 個ぶん程度)。
  (3) **減るのは wall (GPU 側) だけ** — 全隠しの wall が +2.458 → +1.555 → +0.441 と単調に減る。
  統合メッシュが毎描画シェーディングする頂点が減るぶん効く。
  (4) **全表示の得は両指標とも単調に失われる** (メインスレッド -1.390 → -0.985 → -0.352 → -0.003)。
  つまり**メインスレッドで見ると除外は純損**。分岐点も k でほぼ動かない (91.1/91.5/88.4/94.7%)。
  → [U10] の D2 を「除外を積極的に勧めない。GPU がボトルネックと分かっている場合の逃げ道」へ弱めた。
  README も同趣旨へ直した。
  (5) 副産物: F モードは A/B を「頂点数降順の正準順序」1 本で駆動する。従来は A がパス辞書順・
  B が AAO 改名後のシェイプ名順で順序一致の保証が無かったが、k=0 が E3 と一致した
  (メインスレッド半分隠す -0.712 vs -0.718、分岐点 89.8% vs 91.1%)。この機体では両順序が
  同じ 4 件を隠すため。**既存計測の half-hidden は結果として正しかった。**
  ハーネスは `Tools/MTLab/Vertex perf F k=0/3/6/9` (gitignore 対象)。
  落とし穴: AAO の統合時の改名は**前置だけとは限らない** (RenameToAvoidConflict)。
  MT_Hide_* シェイプの突き合わせは末尾一致では落ちるので、部分一致かつ一意で引く

- C: [U14] 隠している間のコストへ**機構で手を打つ**ことにし、[U10] の D1 を開き直した
  (2026-09-02、ユーザーの明示指示による再検討)。**P2a「隠れたマテリアルスロットの差し替え」を採用し 0.9.0-alpha で実装した (2026-09-03、`emptyHiddenMaterialSlots`)**:
  AAO の統合後 (Optimizing、AAO の後ろ) に、統合メッシュの各スロットについて「頂点を覆う隠蔽シェイプ
  (所有トグル) が全部隠れたら」マテリアルを描画パスの無い `MT_Empty` へ PPtr カーブで差し替える。
  所有トグルが 1 つならそのクリップへ、複数なら AAP の AND ゲートで。SMR もスロット数も増えない。
  効く粒度はマテリアルの共有単位 (Shinano は衣装 7 点が 1 マテリアルなので全隠しでしか効かない。
  MUMUS_all は 16 スロット中 11 が差し替え対象で、半分隠す状態の損 wall +5.0 → +0.6 (判定不能)、
  全隠し +8.0 → +0.3 (判定不能)、メインスレッド +4.1 → +0.6)。
  当初「スロット単価 0.5 µs」で却下していたのは算術の前提 (1 パス) が誤りで、lilToon は約 3 パス。
  **却下した 1a** (初期非表示トグルをゲート付き別レンダラーへ統合): 実測では全隠しの損が全指標で
  消えたが、グループが全部隠れた状態でしか効かず、SMR +1 とスロット統合の一部放棄を伴うため
  ユーザー判断で却下。0 から列挙し直した案の表と、P2b (トグルごとにマテリアルを分ける変種、
  スロット数が増える) の実測は `Docs/hidden-cost-revisit-2026-09-02.md` §2b・§4.4〜4.5
- C: [U13] 目標 B の核心（レンダラー個数 vs マテリアルスロット数の切り分け）が決着した
  (2026-08-30)。**追加計測はしていない** — 目標 F の k 系列が、除外 1 件ごとに SMR が
  1 個ずつ戻る掃引そのものだったため、既存データから取れた。
  k=0 → k=9 で SMR +9・スロット +8 に対し全表示の CPU メインスレッドの d が +1.387 ms 動く。
  目標 A のスロット単価 0.172 ms/スロット (10 体・33 描画) を 9 描画へ換算した
  0.0469 ms/スロット × 8 = 0.375 ms を差し引くと、レンダラー 9 個ぶんが 1.012 ms。
  **レンダラー 73% / スロット 27%**、レンダラー単価は 1.25 µs/レンダラー/体/描画。
  **目標 A が別経路 (合成メッシュ・Standard シェーダ・スロット単独掃引) で出した
  69% : 31% を再現した** — 実アバター・lilToon・レンダラー＋スロット同時掃引という
  独立な経路で同じ切り分けが出たことになる。
  残る不確かさ: 区間ごとの傾きは 1 トグルあたり +0.135/+0.211/+0.116 とばらつき、
  各 d の noise (0.13〜0.28) に対し区間差を分離できていない (確かなのは全体の傾きだけ)。
  スロット単価が Standard シェーダでの値なので lilToon では変わり得る点も引き継ぐ。
  目標 B の残り (ボーン数・skinWeights・シェイプ本数・bounds/UWO・影・プローブ) は未測定

## Next

0. 0.9.0-alpha のコミット (ユーザー承認後)。その後、実機 (VRChat クライアント) で差し替えの動作確認
   (隠したスロットが描かれないこと・Safety でシェーダがブロックされたときの見え方) は未実施
0b. `MTVertexPerf` の無人連鎖 (`run_g_chain.ps1`) にも Interaction Mode の切り替えを入れる
   (今は `MTSlotSwapPlay` だけ)。検査用の合成アバター (双方向・片方向・共有スロット) を作れば
   実行検査の not-run 12 を消せる
   (`Docs/hidden-cost-revisit-2026-09-02.md` §6)
1. Quest 実機での再確認 (デルタを有限値へ変えたので、∞ 前提の 2026-08-25 の確認は
   取り直しになる) — blocked-by: ユーザー実施
2. 実機での体感確認 (再表示時にレスト位置から揺れ直す見え方が許容範囲か) — blocked-by: ユーザー実施
3. Android で CPU/GPU どちらのスキニング経路を通るかの調査 (`Docs/perf-research-backlog.md` §8)。
   目標 G はこれが CPU 経路だった場合にだけ意味を持つ。**Next 1 とは別の問いで、実機の
   正しさ検査を待つ必要はない** (Android ビルドで `MeshSkinning.Skin` と
   `MeshSkinning.GPUSkinning` のどちらが値を持つかを見るだけ)。後回しというユーザー判断 (2026-08-30)

## Paths

- C: `Docs/hidden-cost-revisit-2026-09-02.md` — 隠している間のコストの再検討 ([U14] の正本。
  18 案の比較表、E2E と系列 G / S / W の実測、採用の判断、実装の検証 (§4.7)、公開契約への影響、未測定事項)
- C: `../../DevProject/Assets/_MTLab/Editor/MTSlotSwapE2E.cs` (静的検査、batchmode) /
  `MTSlotSwapPlay.cs` (実行検査、GUI Play) / `../../DevProject/MTLabOut/run_slot_play.ps1` (無人起動。
  pwsh 7、時刻付きの進捗ログと 5 分無進捗停止) — 0.9.0-alpha の検証足場 (2026-09-03)
- C: `Docs/hidden-cost-decision.md` — 隠している間のコストへ手を打つかの決定 ([U10] の正本。
  3 分解・AAO の Activeness 統合・頂点数ベースの分岐点・却下した機構の一覧)
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
  **0.9.0-alpha の足場は 2026-09-02 に追加**: `Assets/_MTLab/Editor/MTHiddenGroupE2E.cs`
  (OFF / ON / ALL の 3 ビルドでグループ SMR の構成を検査。結果 `MTLabOut/hidden_group_e2e.txt`) と
  `MTVertexPerf.cs` の `Tools/MTLab/Vertex perf G` (F k=0 と同構図で B の全トグルを初期非表示にして
  ゲート付きグループへ入れ、状態適用でレンダラーの enabled を AND ゲートどおりに切る。
  結果 `MTLabOut/vertex_perf_G.txt`)。G 以外の系列は `separateInitiallyHiddenToggles=false` で
  ビルドして従来の B と同じ構成を保つ (Shinano_TEST は初期非表示トグルを 2 つ持つため)。
  落とし穴: 別レーンのセッションが `taskkill /IM Unity.exe` を実行すると DevProject の batchmode も
  巻き込まれて結果なしで消える (2026-09-02 16:17 に踏んだ。Vault の罠ノート参照)。
  長いジョブは結果をアバター/条件ごとに逐次書き出す
  **AAO の統合可否を調べる足場は 2026-08-28 に追加**: `Assets/_MTLab/Editor/MTMergeGate.cs`
  (gitignore 対象)。NDMF Plugin を Optimizing フェーズへ置き、
  `BeforePass("Anatawa12.AvatarOptimizer.Processors.TraceAndOptimizes.AutoMergeSkinnedMesh")`
  で AAO の解析済み BuildContext を掴み、AAO の `FilterMergeMeshes` /
  `CategoryMeshesForMerge` を**実物のまま**呼んで足切り理由・グループ構成・
  アニメーションされているプロパティを出す。結果は `MTLabOut/merge_gate.txt`。
  再構築時の注意 3 つ: (1) AAO プラグイン全体の直前だと解析状態が未生成で使えない
  (2) `GetAllFloatProperties` / `GetAllObjectProperties` は
  `AnimationComponentInfoExtensions` の**静的拡張メソッド**なので、インスタンス
  メソッドとしてリフレクションすると黙って「該当なし」になる (3) 落ちた理由は
  ソース順で最初に当たった条件しか出ない (複数条件へ同時に当たる例がある)

## Resume protocol

0. **0.9.0-alpha (隠れたスロットの差し替え) の続きなら `Docs/hidden-cost-revisit-2026-09-02.md` §4.7〜6 を先に読む**
   (`DevProject/MTLabOut/hidden_group_resume_prompt.md` は却下した 1a 時点の再開手順で、古い)
1. `Docs/research-2026-08-24-next-mechanisms.md` を読む (新機構の再開はこれが正本)
2. git fetch と status で work_branch / upstream の drift を実測してから作業する
3. Unity 検証は DevProject が他セッションに使われていないこと (UnityLockfile 不在) を確認してから
