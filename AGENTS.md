# Agent instructions

kieMergeableToggle（`com.kie.kie-mergeable-toggle`）のリポジトリ。**このフォルダのルートが
そのまま VPM パッケージ**である。

## Scope and precedence

ユーザーの明示的な指示、この文書、ワークサンプルとしての既存コードの順に優先する。
ワークスペース全体の振り分け規則は `../../AGENTS.md` にある。

このリポジトリには実装だけが含まれる。**Unity 上での検証は `../../avatar-dev` で行う**
（`file:` 参照で読まれている）。検証手順とデバッグツールは向こうの規約に従う。

## Project contract

利用者はアバター制作者である。公開契約は次の 3 つで、変更すると利用者の Prefab が壊れる。

- コンポーネント `MergeableToggle`（`Runtime/MergeableToggle.cs`）とそのフィールド名
  （`enableConversion` / `disableComponentsWhenHidden` / `disablePhysBonesWhenHidden` /
  `disableSharedPhysBonesWhenHidden` / `emptyHiddenMaterialSlots` / `excludedPaths` /
  `forceIncludedPaths`）
- ビルド後の生成物の形状（`MT_Hide_…` ブレンドシェイプと `blendShape.` カーブによる
  非表示方法、共有 PB 停止用の `MT_PBStop` レイヤーと `MT_Hidden/…` パラメータ、
  隠しスロット差し替え用の `MT_SlotOff` レイヤー・`MT_Empty` マテリアル・
  `Hidden/kieMergeableToggle/Empty` シェーダ）
- パッケージ ID `com.kie.kie-mergeable-toggle`

内部実装（`Editor/` のクラス構成、走査順序、貪欲セットカバーの詰め方）は自由に変更してよい。

## Dependencies

- `com.vrchat.avatars` ^3.7.0 / `nadena.dev.ndmf` ^1.14.0
- **Modular Avatar への依存を持たない**（0.1.0-alpha で意図的に外した）。MA 由来のトグルは
  検出対象とするが、MA の型にコンパイル時依存を持たせない
- NDMF の `Transforming` フェーズで `AfterPlugin("nadena.dev.modular-avatar")` に入る

## Invariants

- **AAO（Avatar Optimizer）の自動メッシュ統合を妨げない。** 変換後は rootBone を
  変換対象以外の SMR の最頻値（無ければ Humanoid の Hips）へ、localBounds を合併値へ、
  `m_UpdateWhenOffscreen` を false に正規化する。ここが揃っていないと AAO の
  `CategorizationKey` が一致せず、統合されない（これが本パッケージの存在理由である）。
  Hips への固定は、アバター内に既に存在する合意（MA Mesh Settings 等）を壊すため不可
  （0.8.0-alpha で変更。CustomBase で実測）
- **隠蔽シェイプのデルタは有限の遠方値（`1e6`）で作る。** NaN は Unity が格納時に 0 へ潰し、
  `+Infinity` は VRChat クライアントの自分視点でレンダラーごと描画されなくなる
  （2026-08-28 に実機で特定。鏡・エディタ・Av3Emulator では再現しないため、
  ここを ∞ に戻すと気づかぬうちに壊れる）
- メッシュの改変は `Instantiate` + `RegisterReplacedObject` で非破壊的に行う。元のアセットは書き換えない
- ポリゴン数は変更しない。変換はボーンとカーブの付け替えに限る

## Change scope

- 1 つの変更で扱うのは 1 つの関心事に限る。検出（`ToggleScanner`）と変換
  （`MergeableTogglePlugin`）と表示（`MergeableToggleInspector`）を同じコミットに混在させない
- `CHANGELOG.md` は版ごとに記載する。**ロジックを変更していない版は、その旨を明記する**

## Safety and truth

- 実際に実行した検査だけを報告する。skip した検査と未実行の検査は PASS ではない。
  合否は件数付きで記載する
- 実アバターを対象とする検証は必ず複製に対して行う。元本の Prefab を書き換えない
- 明示的な権限なしに push、Release 作成、公開、remote 変更を行わない

## Commands

Unity の検証は `../../avatar-dev` を開いて行う。バッチ実行の前に Unity Editor が同じ
プロジェクトを開いていないことを確認する。

## Release

1. `package.json` の `version` と `CHANGELOG.md` の見出しを合わせてコミットする
2. push 後、GitHub Actions の `Build Release`（`workflow_dispatch`）を手動で実行する
3. `../../vpm-listing` の `Build Repo Listing` が Release を拾い、公開 listing を更新する

`0.x.y-alpha` は prerelease なので、ALCOM で「Show Prerelease Packages」を
オンにした利用者にだけ表示される。

## Handoff maintenance

- 現在の状態は `HANDOFF.agent.md` が正本である。実質的な進捗・判断・検証・blocker が変わったら更新する
- 過去の引き継ぎは、Obsidian Vault の `記録/kieMergeableToggle/引き継ぎ履歴/`（`obsidian_semantic_search` または `obsidian_search_notes` の `folder` で引く）に、1件1ノートで原文のまま残す。上書きしない
- 作業メモをワークスペース直下（`../../`）に置かない

## Documentation routing

- `README.md` is the user-facing usage and contract explanation; `CHANGELOG.md` is release history.
- `HANDOFF.agent.md` is the current state, decisions, evidence, and unresolved items. `HANDOFF.md` is the links index. Prior handoff text and migration records live in the Obsidian Vault under `記録/kieMergeableToggle/引き継ぎ履歴/` (one note per entry; search with `obsidian_semantic_search` or `obsidian_search_notes` using `folder`). `docs/agent-appendix.md` contains task-specific reading conditions and update responsibilities.
- Before handoff or document maintenance, read `docs/agent-appendix.md`. Keep the AAO merge invariants, finite `1e6` delta rationale, non-destructive mesh replacement, and no-polygon-change invariant in this file.
