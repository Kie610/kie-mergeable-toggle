# 引き継ぎ履歴

過去の作業メモを原文のまま置く。現在の状態は `HANDOFF.agent.md` が正本で、
ここは「そのときどう判断したか」を後から追うための記録である。

---

## 2026-08-11 高難度セッションの作業状況

出典: ワークスペース直下へ漂着していた `mergeable-toggle-作業状況.md`。
2026-08-19 にここへ移設した。**当時の記述のままで、現状とは食い違う**
(記載の version は 0.1.0-beta.1・リモート未作成だが、実際は 0.4.0-alpha で公開済み)。


## 完了したこと

### パッケージ確定・リネーム
- 名称確定: **com.kie.kie-mergeable-toggle / "Mergeable Toggle"** (namespace `Kie.MergeableToggle`)
- `Packages\com.kie.hide-by-scale` → `Packages\com.kie.kie-mergeable-toggle` へ移動、
  package.json / asmdef / C# / README / CHANGELOG / DevProject manifest /
  vpm-listing の `githubRepos`(`Kie610/kie-mergeable-toggle`)をすべて更新済み
- version: `0.1.0-beta.1`(リリース準備値)。依存: com.vrchat.avatars ^3.7.0 /
  nadena.dev.ndmf ^1.14.0(**MA 依存は削除した**)

### 設計転換(重要)
当初の方針B「MA Mesh Cutter を生成して MA に処理させる」は**実測で不成立**:
1. MA は NaNimation 時に `m_UpdateWhenOffscreen` へ float カーブを追加
   → AAO `IsAnimatedForbidden` が統合候補から除外
2. AAO `CategorizationKey` は Bounds / RootBone 等の完全一致を要求
   → 服ごとに違うため全部シングルトン
→ ユーザー承認のうえ**自前 NaNimation へ転換**(知見ノート
「2026-08-11 MA NaNimationはAAO自動統合を阻害する.md」参照)

### 現行実装(コンパイル済み・E2E 実測済み)
`Packages\com.kie.kie-mergeable-toggle\`
- `Runtime/MergeableToggle.cs` — ルート用コンポーネント
  (enableConversion / excludedPaths / forceIncludedPaths)
- `Editor/ToggleScanner.cs` — 候補検出。エディタ時は descriptor 走査
  (`Scan`)、ビルド時は AnimationIndex 問い合わせ(`ScanHierarchy`)
- `Editor/MergeableToggleInspector.cs` — Parameter Compressor 流 UI
  (チェックリスト、警告付き候補はデフォルト除外)
- `Editor/MergeableTogglePlugin.cs` — NDMF Transforming、
  BeforePlugin("nadena.dev.modular-avatar")。処理内容:
  1. 貪欲セットカバーでボーン複製(選択ボーンの**子**として生成 →
     入れ子トグルは NaN 伝播で自然に OR になる)、ウェイト付替
     (メッシュは Instantiate + RegisterReplacedObject で非破壊)
  2. m_IsActive カーブを複製ボーンの m_LocalScale 1⇔NaN カーブへ書換
     (**AnimationCurve コンストラクタは NaN キーを捨てるので AddKey +
     オブジェクト初期化子で構築すること**)
  3. 初期非表示トグルは VRCScaleConstraint(weight=NaN)+ 発動クリップで
     IsActive=0
  4. rootBone→Hips / localBounds→合併値 / UWO→false に正規化
     (これで AAO CategorizationKey が揃い自動統合が効く)

### E2E 実測結果(受け入れ条件1クリア)
検証: `DevProject\Assets\Editor\MergeableToggleE2E.cs`(ワンショット、
プロジェクトルートの `mt_e2e_request.txt` で駆動、結果は `mt_e2e_result.txt`)
- **MUMUS_all**: SMR 21→4、ボーン 453→933、ポリゴン不変、
  NaN クリップ 20、m_IsActive バインディング 48→5
- **Shinano**: SMR 11→2、ボーン 272→469、ポリゴン不変、NaN クリップ 7
- 検出はシーン内10体で80候補、警告判定も動作(Milfy のスマホギミック等)

## 残タスク
1. Av3Emulator での目視トグル挙動確認(ビルド統計と NaN 配線の構造証拠は
   取得済みだが、プレイモードの実挙動は未確認)
2. E2E スクリプト(`MergeableToggleE2E.cs`)と `mt_e2e_*.txt` の後始末
3. ~~2リポジトリの初回コミット~~ → **完了**(パッケージ 592e46d /
   listing 3d46ff9、いずれも main、リモート未作成)
4. GitHub リモート作成・Release(`0.1.0-beta.1`)・listing 公開は未実施
   (公開操作はユーザー承認必須)
5. Obsidian への設計判断記録は3件済み(VPM構成 / MA Mesh Cutter 仕組み /
   MA NaNimation と AAO の非互換)

## 環境メモ
- Unity 2022.3.22f1 が DevProject を開いたまま(PID は変動する)。
  バッチモード不可。再コンパイルはウィンドウフォーカスで誘発、検証は
  `Library/ScriptAssemblies` と Editor.log の `error CS` で確認
- unity-mcp (CoplayDev) セットアップ作業中 → 完了後はそちらで直接操作可
