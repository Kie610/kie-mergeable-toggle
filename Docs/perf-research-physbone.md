# PhysBone の負荷要因と停止方法の調査

作成 2026-08-29。対象はアバター上の `VRCPhysBone` / `VRCPhysBoneCollider` である。
ウェブ検索、VRChat SDK 3.10.4 のローカル実体、kieMergeableToggle の既存実測を調べた。
この文書では新しい実測を行っていない。

## 結論: 停止は CPU に効くが Performance Rank には効かない

- C: 負荷を説明する第一の量は、全 PhysBone が動かす `Affected Transforms` の総数である。
  VRChat 公式はこれを独立した Performance Rank 項目にし、コミュニティ実測は
  1,000 transforms あたり 0.66 ms と報告している。本リポジトリの既存実測でも、
  `enabled=false` にした transforms が多い条件ほど `PhysBoneJob` の削減が大きかった。
  出典: [VRChat Performance Ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)、
  [VRChat Wiki Community Performance Benchmarks](https://wiki.vrchat.com/wiki/Community:VRChat_performance_benchmarks)、
  `D:\GitHub_WorkSpace\VRC\DevProject\MTLabOut\pb_perf.txt`
- C: `VRCPhysBone.enabled=false` は計算を止める。本リポジトリでは Shinano の 35 transforms
  を止めて 0.649 ms、MUMUS_all の 358 transforms を止めて 3.934 ms の
  `PhysBoneJob` を削減した。一方、VRChat 公式は無効な GameObject / Component も
  Performance Rank に数えると明記している。したがって本パッケージの停止機能は
  実行時 CPU 負荷を下げるが、アップロード済みアバターのランクを改善しない。
  出典: `D:\GitHub_WorkSpace\VRC\DevProject\MTLabOut\pb_perf.txt`、
  [VRChat Performance Ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)
- C: ランクも下げる必要がある場合は、ビルド結果から PhysBone / Collider を削除し、
  Affected Transforms と Collision Check Count も減らす必要がある。衣装の表示中に
  PhysBone が必要な本パッケージでは削除できないため、`enabled` のアニメーションは
  目的に合った最小の手段である。出典: `Editor/PhysBoneStopper.cs`、
  [VRChat Performance Ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)

## 調査範囲と限界

- C: ウェブ検索は利用できた。一次情報として VRChat Creation 公式ドキュメント、
  VRChat 公式ブログ、SDK リリースノート、Unity 公式 API を優先した。数値を伴う
  二次情報は VRChat Wiki のコミュニティ実測だけを採用した。
- C: ローカル SDK は `com.vrchat.base` / `com.vrchat.avatars` ともに 3.10.4 である。
  公開フィールド、初期化コード、ランク閾値アセット、上限定数は読めた。
  出典: `D:\GitHub_WorkSpace\VRC\DevProject\Packages\com.vrchat.base\package.json`、
  `D:\GitHub_WorkSpace\VRC\DevProject\Packages\com.vrchat.avatars\package.json`
- U: ソルバー本体と Performance Rank の走査本体は
  `com.vrchat.base/Runtime/VRCSDK/Plugins/VRC.SDK3.Dynamics.PhysBone.dll` と
  `VRCSDKBase-Editor.dll` にあり、C# ソースは同梱されていない。各設定がどの分岐や
  SIMD job を増減させるかは、読めたソースだけでは確定できない。
- C: 既存実測の採用条件は、`pb_perf.txt` で準備、期待 `enabled` 状態、反復が成立した
  run に限った。`PREPARE FAILED`、期待状態の検証失敗、結果欄に「無効」とある run は
  数値根拠に使っていない。出典: `D:\GitHub_WorkSpace\VRC\DevProject\MTLabOut\pb_perf.txt`、
  `HANDOFF.agent.md`

## 負荷は transforms、衝突候補、更新回数へ分けて考える

### Affected Transforms が基本の仕事量になる

- C: `Affected Transforms` は、全 `VRCPhysBone` が影響する Transform 数の合計である。
  1 コンポーネントは root と子を含め最大 256 transforms まで扱う。分岐した複数チェーンも
  1 コンポーネントに含められる。出典:
  [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- C: 公式は PhysBone をマルチスレッド処理として説明している。1 コンポーネントへ
  すべてを詰めるのが常に最速とは限らず、1 コンポーネントが 128 transforms を超える
  あたりでは 2～3 個へ分ける目安も示している。ただしこれはランク閾値ではない。
  出典: [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- C: コミュニティ実測では 1,000 Affected Transforms あたり 0.66 ms、コンポーネント数と
  階層形状の両極端で最大 33% の差が報告された。測定ページには PhysBone 部分の機材、
  VRChat build、反復数が明記されていないため、係数を本パッケージの保証値には使えない。
  出典: [VRChat Wiki Community Performance Benchmarks](https://wiki.vrchat.com/wiki/Community:VRChat_performance_benchmarks)
- A: 同じ Affected Transforms 総数なら、コンポーネント数は job 分割と固定費の両方へ効く。
  「少ないほど常に速い」という単調な関係とは限らないため、チェーンの偏りを含めて測る
  必要がある。根拠は公式のマルチスレッド説明で、最適点は未測定である。
- U: チェーンの長さと本数を同じ Affected Transforms 総数で交換したときの差、分岐幅、
  階層深さの寄与は未測定である。

### 衝突コストは transform と collider の到達可能な組み合わせで増える

- C: Performance Rank の `PhysBones Collision Check Count` は、各 collider が影響できる
  PhysBone transforms 数の総和である。同じ transform が複数 collider の対象なら重複して
  数える。実際の参照関係ごとの組み合わせ数であり、単純な「総 transforms × 総 colliders」
  とは一致しない。出典: [VRChat Performance Ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)
- C: 2026.2.2 で、どの PhysBone にも割り当てられていない avatar collider がランクへ
  誤計上される不具合が修正された。現在は未参照 collider を collision workload の代理値へ
  入れない扱いである。出典: [VRChat 2026.2.2 release notes](https://docs.vrchat.com/docs/vrchat-202622)
- C: collider 形状は Sphere / Capsule / Plane の 3 種である。Global Collision は
  Sphere / Capsule だけが対応する。標準 body collider は Performance Rank に数えない。
  出典: [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- C: `Allow Collision=false` は衝突処理全体を切る設定ではない。明示 `Colliders` リストとの
  衝突は残し、global collider などリスト外の collider を拒否する。`True` と `Other` は
  他 avatar / world を含む候補範囲を広げ得る。出典:
  [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- A: ランクの Collision Check Count は avatar 内の静的な参照関係を表すが、実行時の
  `Allow Collision` が受け入れる他 avatar / world の global collider 数までは表さない。
  混雑時の実コストは同じ avatar 単体のランク値から増え得る。
- C: コミュニティ実測は collider 数の影響を「非常に小さい」と報告したが、形状別の
  数値は掲載していない。出典:
  [VRChat Wiki Community Performance Benchmarks](https://wiki.vrchat.com/wiki/Community:VRChat_performance_benchmarks)
- U: Sphere / Capsule / Plane の 1 check あたりの差、`Inside Bounds`、`Bones As Sphere`、
  半径や capsule height の差は未測定である。DLL 内の形状別アルゴリズムも確認できない。

### Grab / Pose と力学パラメータの差は、機能は確認できるが負荷量は未確定

- C: `Allow Grabbing` と `Allow Posing` は、自分と他人による操作を個別に許可する。
  PhysBone は接触・grab 探索用の bounds を bone の動きに応じて更新し、最大 10×10×10 m に
  制限される。radius が 0 より大きく collision 対象の bone だけが bounds に入る。
  出典: [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- A: Grab / Pose を禁止すれば対人操作の候補探索または状態更新の一部は省ける可能性がある。
  ただし通常の chain simulation まで止まる設定ではないため、主コストである transform 更新は
  残ると考える。
- U: `Allow Grabbing` / `Allow Posing` の on/off、Self / Others のフィルタごとの CPU 差は
  未測定である。公式資料にも数値はない。
- C: `Pull`、`Spring`、`Stiffness`、`Gravity`、`Immobile` などは chain の運動を決め、
  Integration Type により使う数学と表示される設定が変わる。avatar では初期化時に設定され、
  通常は実行中にアニメーションしない。出典:
  [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- C: limit は collider より高性能だと公式が案内している。Polar limit は非ゼロのコストを持ち、
  64 を大きく超える利用を避け、近い pitch / yaw なら低コストの Angle を使うよう勧めている。
  出典: [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- C: コミュニティ実測は transforms、コンポーネント構成、collider 数以外の設定に
  目立つ差がなかったと報告する。ただし個別パラメータの系列と検出限界は掲載されていない。
  出典: [VRChat Wiki Community Performance Benchmarks](https://wiki.vrchat.com/wiki/Community:VRChat_performance_benchmarks)
- A: `Pull=0` や `Gravity=0` で一部の演算が短絡される可能性はあるが、chain の transform
  読み書きと積分そのものは残る。パラメータ値より Affected Transforms の削減を先に測る。
- U: Basic / Advanced Integration、Gravity、Immobile、Stiffness、Pull、Spring、Stretch / Squish、
  None / Angle / Hinge / Polar limit の各 on/off が何演算または何 ms を増やすかは未測定である。

### Is Animated は毎フレーム rest pose を読み直す

- C: `Is Animated` を有効にすると、PhysBone chain の root を含む bone の rest position が
  animation に従い、毎フレーム更新される。無効ならその読み直しを行わない。出典:
  [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- A: `Is Animated=true` は chain ごとの rest pose 取得と反映を増やすため、同じ transforms 数でも
  false より重い可能性がある。差の方向と大きさは実測が必要である。
- U: `Is Animated` の transforms あたりの追加時間は未測定である。SDK 3.1.12 で同設定時の
  jitter 修正履歴はあるが、性能値はない。出典:
  [SDK 3.1.12 release notes](https://creators.vrchat.com/releases/release-3-1-12/)

### avatar の更新タイミングは公開資料から確定できない

- C: world 専用の `VRCPhysBoneRoot` には Automatic / Fixed Time / Real Time がある。
  Fixed は Unity `FixedUpdate()` 相当、Real Time は可変時間の `Update()` 相当、Automatic は
  transform の動きに応じて両者を切り替える。出典:
  [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- U: `VRCPhysBoneRoot` は world 専用で、avatar には timing の公開設定がない。avatar の
  PhysBone が常にフレームごとか、固定 step を挟むか、状況で切り替えるかは、公開資料と
  同梱 C# ソースから確定できない。
- A: 実測では `PhysBoneJob` の frame marker とフレーム時間を記録し、描画 FPS と
  `Time.fixedDeltaTime` を独立に振れば、呼び出し回数が frame 依存か fixed step 依存かを
  判別できる。

## Performance Rank とハード上限

- C: 2026-08-29 時点の公式値とローカル SDK 3.10.4 の閾値アセットは一致する。

| 項目 | PC Excellent | PC Good | PC Medium | PC Poor | Mobile Excellent | Mobile Good | Mobile Medium | Mobile Poor |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| PhysBone Components | 4 | 8 | 16 | 32 | 0 | 4 | 6 | 8 |
| Affected Transforms | 16 | 64 | 128 | 256 | 0 | 16 | 32 | 64 |
| PhysBone Colliders | 4 | 8 | 16 | 32 | 0 | 4 | 8 | 16 |
| Collision Check Count | 32 | 128 | 256 | 512 | 0 | 16 | 32 | 64 |

  出典: [VRChat Performance Ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)、
  `D:\GitHub_WorkSpace\VRC\DevProject\Packages\com.vrchat.base\Runtime\VRCSDK\Dependencies\VRChat\Resources\Validation\Performance\StatsLevels\Windows\`、
  `...\StatsLevels\Quest\`
- C: Mobile の Poor 値は表示ランクだけでなくハード上限でもある。8 components、
  64 Affected Transforms、16 colliders、64 collision checks のどれかを超えると、Show Avatar
  でも回避できず、該当する Avatar Dynamics components が VRChat 内で除去される。
  出典: [VRChat Performance Ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)
- C: プラットフォーム共通のビルド上限は `VRCPhysBone` 256 個、
  `VRCPhysBoneCollider` 256 個である。avatar 上で Global Collision を有効にできる collider は
  最大 4 個、1 PhysBone が扱える transforms は最大 256 個である。出典:
  [SDK 3.2.2 release notes](https://creators.vrchat.com/releases/release-3-2-2/)、
  `D:\GitHub_WorkSpace\VRC\DevProject\Packages\com.vrchat.base\Runtime\VRCSDK\Dependencies\VRChat\Scripts\Validation\AvatarValidation.cs`、
  [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)
- C: PC で PhysBone 系のランク閾値によるフィルタが発動すると、PhysBone、Collider、Contact
  components は除去される。PC の既定 Minimum Displayed Performance Rank は Very Poor なので、
  既定設定ではこの理由による除去はない。出典:
  [VRChat Performance Ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)

## 止めかたで CPU とランクへの効果が分かれる

| 方法 | PhysBone の計算コスト | Performance Rank | 根拠の強さ |
|---|---|---|---|
| `VRCPhysBone.enabled=false` | 停止する。既存実測で `PhysBoneJob` 減少 | **変わらない**。無効 component も数える | C |
| PhysBone の GameObject / 親を inactive | Unity 上は component が無効になり `Update()` が呼ばれない。PhysBone 固有の同等性は未測定 | **変わらない**。無効 GameObject も数える | C + A |
| component をビルド時に削除 | component と chain simulation が存在しない | component 数と、その component 由来の transforms / checks が減る | C |

- C: `enabled=false` の停止時に rest pose へ戻すかは `Reset When Disabled` で選べる。
  「disabled」が正式な状態遷移として PhysBone に実装されていることも確認できる。
  出典: [VRChat PhysBones](https://creators.vrchat.com/common-components/physbones/)、
  [SDK 3.1.12 release notes](https://creators.vrchat.com/releases/release-3-1-12/)
- C: 本パッケージは単独所有 PhysBone の `VRCPhysBone.m_Enabled` を衣装の隠蔽カーブで
  1↔0 にし、共有 PhysBone は全 owner が隠れたときだけ専用 layer で 0 にする。
  GameObject を inactive にしないため、同じ armature 上の別衣装や Renderer まで巻き込まない。
  出典: `Editor/PhysBoneStopper.cs`、`Docs/hiding-mechanisms.md`
- C: Unity は GameObject の inactive 化で attached component を無効にし、script の
  `Update()` を呼ばず、`OnDisable()` を発火させる。出典:
  [Unity GameObject.SetActive](https://docs.unity3d.com/ja/current/ScriptReference/GameObject.SetActive.html)
- A: PhysBone GameObject の inactive 化も chain simulation を止めると考えられるが、
  `enabled=false` と比べた manager 側の登録解除時間、再有効化時間、残る固定費は未検証である。
- U: `enabled=false` と GameObject inactive の定常 CPU 差、切替 1 回あたりの spike、GC、
  再表示直後の pose 差は未測定である。
- C: component 削除だけがランク改善につながる。公式の「disabled も数える」という規則から、
  `enabled=false` や inactive をアップロード前に設定してもランク逃れにはならない。
  出典: [VRChat Performance Ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/)

## 本リポジトリの既存実測

- C: 2026-08-25 の直接停止実測は Unity 2022.3.22f1、Play、240 frames 平均、keep / stop を
  3 回交互に測った。Shinano は 61 components / 136 transforms のうち 10 / 35 を止め、
  `PhysBoneJob` が 2.523→1.874 ms、0.649 ms（25.7%）減った。keep の半レンジは
  ±0.113 ms である。出典: `D:\GitHub_WorkSpace\VRC\DevProject\MTLabOut\pb_perf.txt`
- C: 同条件の MUMUS_all は 89 components / 414 transforms のうち 68 / 358 を止め、
  `PhysBoneJob` が 4.662→0.728 ms、3.934 ms（84.4%）減った。keep の半レンジは
  ±0.085 ms である。出典: `D:\GitHub_WorkSpace\VRC\DevProject\MTLabOut\pb_perf.txt`
- C: 共有 PhysBone の追加回収を測った成立 run では、Shinano 0.608 ms、MUMUS_all
  0.248 ms をさらに回収した。共有停止用 Animator layer の可視時代償は Shinano
  +0.007 ms（ノイズ以下）、MUMUS_all +0.053 ms（有意）だった。出典:
  `D:\GitHub_WorkSpace\VRC\DevProject\MTLabOut\pb_perf.txt` の末尾の成立 run、
  `HANDOFF.agent.md`
- C: `pb_census.txt` は保守判定で停止できる単独所有 PhysBone を Shinano 10/61、
  MUMUS_all 68/89 と数えた。現行ガード追随後は共有分を含む期待値が 28 / 75 である。
  これは安全に止められる chain の母集団確認である。負荷は測っていない。出典:
  `D:\GitHub_WorkSpace\VRC\DevProject\MTLabOut\pb_census.txt`、`HANDOFF.agent.md`
- U: 上記 2 avatar の差から「1 transform あたり何 ms」と一般化はできない。停止した chain は
  長さ、分岐、collider、設定、job 分割が同時に違う。測定環境も Unity Editor 内の
  SDK simulation であり、VRChat クライアント本体ではない。

## 実測の計画

共通して A/B を隣接した対で測る。反復ごとの対の差
`d = 停止あり − 停止なし` を集め、`|平均 d|` が `d` の半レンジを超え、かつ符号が
全反復で揃ったときだけ有意とする。満たさなければ「判定不能」と書く。

- C: **停止方法の基準線** — 同一 chain について通常、`enabled=false`、GameObject inactive、
  component 削除済みの 4 条件を作る。通常との `PhysBoneJob` 差、main thread、切替 frame の
  spike、再表示後 60 frames を記録する。ランク値は build 後 Avatar Stats で別に記録する。
- C: **Affected Transforms** — collider と設定を固定し、1 chain の長さを 4 / 8 / 16 / 32 / 64、
  chain 本数を 1 / 2 / 4 / 8 と振る。総 transforms が同じ組も作り、長さ、本数、総数を分離する。
- C: **コンポーネント分割** — 総 transforms と階層を固定し、1 / 2 / 4 components へ分ける。
  128 transforms 前後を厚く測り、公式の分割目安がこの環境でも成立するかを見る。
- C: **collision checks** — transforms を固定し、明示 collider を 0 / 1 / 2 / 4 / 8 と振る。
  Avatar Stats の Collision Check Count と実時間の傾きを同時に取り、候補対数との関係を見る。
- C: **collider 形状** — 同じ位置と近似寸法で Sphere / Capsule / Plane を差し替える。
  `Inside Bounds` と `Bones As Sphere` は形状系列と混ぜず、別の二値比較にする。
- C: **外部 collision** — 明示 collider を空にし、`Allow Collision` の False / Self / Others を
  比較する。他 avatar 数と global collider 数を固定し、0 / 少数 / 多数の別系列にする。
- C: **grab / pose** — collision 条件を固定し、Allow Grabbing と Allow Posing の 2×2 を測る。
  idle、grab 中、pose 中を別区間にし、操作していない定常費と操作中の費用を混ぜない。
- C: **力学設定** — transforms と collider を固定し、Basic / Advanced、Gravity 0 / 非 0、
  Immobile 0 / 非 0、Pull / Spring / Stiffness 0 / 非 0 を一因子ずつ振る。値は挙動が破綻しない
  代表値を事前固定し、探索中に変えない。
- C: **limit** — None / Angle / Hinge / Polar を同じ chain で比較する。Polar 数を増やす系列を
  別に作り、公式が注意する 64 付近を挟む。ただし 64 は概数なので閾値とは扱わない。
- C: **Is Animated** — 同じ animation を流したまま flag だけを false / true にする。
  animation 自体の Animator cost を空 chain 条件で引き、PhysBone 側の追加分を分ける。
- C: **更新頻度** — targetFrameRate / vSync と `Time.fixedDeltaTime` を独立に振り、1 秒あたりの
  `PhysBoneJob` invocation 数と総時間を数える。avatar と、timing を指定できる world の
  VRCPhysBoneRoot を分けて測る。
- C: **実クライアント確認** — Unity Editor の傾向が出た項目だけ VRChat PC client で再測定し、
  local / remote avatar、鏡、人数を固定する。Editor 値を client の絶対値として流用しない。

この計画で最初に測るべきなのは、Affected Transforms、collision checks、停止方法の 3 系列である。
いずれも本パッケージが直接変えられる量であり、ランク値と `PhysBoneJob` の両方へ接続できる。
細かな力学パラメータは、この 3 系列の残差が実用上大きいと分かってから測ればよい。
