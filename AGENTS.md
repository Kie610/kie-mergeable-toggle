# Agent instructions

kieMergeableToggle（`com.kie.kie-mergeable-toggle`）のリポジトリ。**このフォルダのルートが
そのまま VPM パッケージ**である。

## Scope and precedence

ユーザーの明示指示、この文書、ワークサンプルとしての既存コード、の順に優先する。
ワークスペース全体の振り分け規則は `../../AGENTS.md` にある。

このリポジトリは実装だけを持つ。**Unity 上での検証は `../../DevProject` で行う**
（`file:` 参照で読まれている）。検証手順とデバッグツールは向こうの規約に従う。

## Project contract

利用者はアバター制作者。公開契約は次の 3 つで、変えると利用者の Prefab が壊れる。

- コンポーネント `MergeableToggle`（`Runtime/MergeableToggle.cs`）とそのフィールド名
  （`enableConversion` / `excludedPaths` / `forceIncludedPaths`）
- ビルド後の生成物の形（複製ボーンの位置づけ、NaN スケールによる隠しかた）
- パッケージ ID `com.kie.kie-mergeable-toggle`

内部実装（`Editor/` のクラス構成、走査の順序、貪欲セットカバーの詰め方）は自由に変えてよい。

## Dependencies

- `com.vrchat.avatars` ^3.7.0 / `nadena.dev.ndmf` ^1.14.0
- **Modular Avatar への依存は持たない**（0.1.0-alpha で意図して外した）。MA 由来のトグルは
  検出対象だが、MA の型へコンパイル時依存を作らない
- NDMF の `Transforming` フェーズで `BeforePlugin("nadena.dev.modular-avatar")` に入る

## Invariants

- **AAO（Avatar Optimizer）の自動メッシュ統合を阻害しない。** 変換後は rootBone を Hips へ、
  localBounds を合併値へ、`m_UpdateWhenOffscreen` を false へ正規化する。ここが揃わないと
  AAO の `CategorizationKey` が一致せず、統合されない（これが本パッケージの存在理由）
- **NaN キーの構築は `AddKey` + オブジェクト初期化子で行う。** `AnimationCurve` の
  コンストラクタは NaN キーを捨てる
- メッシュの改変は `Instantiate` + `RegisterReplacedObject` で非破壊に行う。元アセットを書き換えない
- ポリゴン数を変えない。変換はボーンとカーブの付け替えに限る

## Change scope

- 1 つの変更で触るのは 1 つの関心事に限る。検出（`ToggleScanner`）と変換
  （`MergeableTogglePlugin`）と表示（`MergeableToggleInspector`）を同じコミットへ混ぜない
- `CHANGELOG.md` は版ごとに書く。**ロジックを変えていない版はその旨を明記する**

## Safety and truth

- 実際に実行した検査だけを報告する。skip した検査と未実行の検査は PASS ではない。
  合否は件数付きで書く
- 実アバターを対象にする検証は必ず複製へ行う。元本の Prefab を書き換えない
- 明示的な権限なしに push、Release 作成、公開、remote 変更を行わない

## Commands

Unity の検証は `../../DevProject` を開いて行う。バッチ実行の前に Unity Editor が同じ
プロジェクトを開いていないことを確かめる。

## Release

1. `package.json` の `version` と `CHANGELOG.md` の見出しを合わせてコミットする
2. push 後、GitHub Actions の `Build Release`（`workflow_dispatch`）を手で実行する
3. `../../vpm-listing` の `Build Repo Listing` が Release を拾って公開 listing を更新する

`0.x.y-alpha` は prerelease なので、ALCOM で「Show Prerelease Packages」を
オンにした利用者にだけ見える。

## Handoff maintenance

- 現在の状態は `HANDOFF.agent.md` が正本。実質的な進捗・判断・検証・blocker が変わったら更新する
- 過去の作業メモは `handoff-history.md` へ原文のまま残す。上書きしない
- 作業メモをワークスペース直下（`../../`）へ置かない
