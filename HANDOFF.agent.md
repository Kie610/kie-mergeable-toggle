# Agent handoff v1

updated: 2026-08-19
repo: D:/GitHub_WorkSpace/VRC/Packages/com.kie.kie-mergeable-toggle (origin = github.com/Kie610/kie-mergeable-toggle)
work_branch: main
upstream: origin/main (同期済み)
base: main@49e6d5d
goal: 手書きのメッシュトグルを AAO が統合できる隠しかたへ機械的に変換する

## State

complete:
- C: 0.4.0-alpha 公開済み。一覧を持ち主ごとの折りたたみにした回 (検出・変換のロジックは無変更)
- C: 変換の実装一式 (検出 `ToggleScanner` / 変換 `MergeableTogglePlugin` / 表示 `MergeableToggleInspector`)
- C: MA Merge Animator 経由のトグルの検出 (0.2.0-alpha)

verified:
- C: 2026-08-11 — evidence: status=PASS; kind=runtime; scope=実アバター 2 体への E2E 変換; counts=MUMUS_all で SMR 21→4・ボーン 453→933・ポリゴン不変・NaN クリップ 20・m_IsActive バインディング 48→5 / Shinano で SMR 11→2・ボーン 272→469・ポリゴン不変・NaN クリップ 7。検出はシーン内 10 体で 80 候補。詳細は `handoff-history.md`

not-run:
- U: Av3Emulator でのトグル挙動の目視確認。構造の証拠は取れているが、プレイモードの実挙動は未確認 (2026-08-11 時点で残タスク・以後の実施記録なし)

## Decisions

- C: MA Mesh Cutter を生成して MA に処理させる案 (方針B) は不成立。MA が NaNimation 時に
  `m_UpdateWhenOffscreen` へ float カーブを足すため AAO の `IsAnimatedForbidden` に当たり、
  さらに AAO の `CategorizationKey` が Bounds / RootBone の完全一致を要求するため服ごとに
  シングルトンになる。実測のうえ自前 NaNimation へ転換した (2026-08-11)
- C: Modular Avatar への依存は持たない (0.1.0-alpha で除去)
- C: 変換後は rootBone→Hips / localBounds→合併値 / UWO→false へ正規化する。
  AAO の統合条件を揃えることが目的

## Next

1. Av3Emulator での目視確認 — blocked-by: none (ユーザーの実機作業)
2. 検証用に DevProject へ置いた E2E スクリプトと一時ファイルの後始末 — 現存を要確認

## Paths

- C: `handoff-history.md` — 2026-08-11 の作業メモ (原文)
- C: `Docs/hiding-mechanisms.md` — 隠しかたの比較 (利用者向け)
- C: `../../DevProject` — Unity 検証プロジェクト
