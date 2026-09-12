# Agent appendix

## 正本と読む条件

- `AGENTS.md`: 常時守る規則、公開契約、AAO/物理不変条件。実装開始時に読む。
- `README.md`: 利用者向け仕様・対応環境・既知の未検証事項。利用方法や契約を変更するときに読む。
- `CHANGELOG.md`: 版ごとの変更とリリース境界。リリース記録を変更するときに読む。
- `HANDOFF.agent.md`: 現在の進捗、判断、検証、未解決事項。引き継ぎ・再開・handoff更新時に読む。
- `docs/handoff-history.md`: 過去原文と移行記録。過去 evidence の確認や schema 変換時に読む。
- `../../avatar-dev` / `../../avatar-dev-quest`: Unity検証先。実装変更時は対象アバターを複製して検証する。

## 更新責任

仕様・公開契約は `AGENTS.md` と `README.md`、版の事実は `CHANGELOG.md`、現在の状態は `HANDOFF.agent.md`、過去状態は `docs/handoff-history.md` が正本である。担当者は変更した正本を同じ作業で更新し、共有状態は親へ証拠とともに返す。親が差分・件数・未実行理由を検品して統合する。

## 検証と境界

compile・runtime・hardwareを分け、skipped/not-runをPASSにしない。過去の自由文 evidence から不足する command/environment/scope/counts は推測しない。不足する検証はHANDOFFのU項目と履歴原文へ残す。管理文書のみの更新ではUnityを実行しない。

変更後は `C:/Users/Kie/.codex/skills/agent-handoff/scripts/validate_handoff.py --root .`、`git diff --check`、限定diff、リンク、AGENTSの行数・UTF-8 LF bytesを確認する。

## 移行採否

ai-project-management 0.5.0 は C/A/U、現行handoff schema、証拠の件数化、正本導線、親の検品・統合を採用する。既存のアバター複製検証、AAO不変条件、非破壊方針、公開境界に適合させる。配布製品に不要なbranch/worktree変更や外部公開手順は不採用とし、理由を `docs/handoff-history.md` に記録する。

## ビルド時間を反復比較するとき

ビルド直後に `Undo.ClearAll()` を実行する。1体の検査が終わってから `MTSlotSwapE2E.PurgeGeneratedClips()` で残ったクリップを破棄する。ビルド直後のclip破棄は検査対象まで壊すため行わない。ON/OFFを交互にし、同条件の反復対照で計測系が成立することを先に確認する。

根拠は `handoff-history.md` の2026-09-04検証記録。Undo累積下の約3分という旧値と、対策後の約105秒を混同しない。記録値は104.18 / 105.60 / 112.89 / 110.77 sで、差し替えのコストは符号が逆のため判定不能。今回は再実測していない。既存の検証環境AGENTSの同じ手順も確認する。

GUI Unityで無人検証を実行するときは、検査入口で EditorPrefs `"InteractionMode"=1`（No Throttling）を用いる既存の対策を確認する。Defaultの非アクティブ時の間引きを製品性能と混同しない。`MTVertexPerf` の `run_g_chain.ps1` への適用はHANDOFFの残件として管理する。
