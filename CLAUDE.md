# unity-few-teeth-tracking — 引き継ぎメモ（Claude Code 用）

M3T 歯列トラッカー（`../few_teeth_tracking/tracker_four_group.py`）の表示・操作側。
全体像・PC ごとの設定・評価手順・既知の問題は **`../few_teeth_tracking/CLAUDE.md`** を先に読むこと。

## スクリプトの役割（Assets/Script）

- `M3TProcessController` — conda 経由でトラッカーを起動/停止。環境名はシーンの `condaEnvironmentName`（既定 `pym3t`）を
  環境変数 `M3T_CONDA_ENV` で上書き可。環境変数はプロセス → ユーザー → マシンの順で読み、`M3T_*` / `PYM3T_*` は
  レジストリの値も子プロセスへ転送する（Unity 起動後に setx した値でも Unity Hub を再起動せずに効く）。
  トラッカーの stdout は `%LocalAppData%Low/DefaultCompany/<product>/m3t_tracker.log`。先頭 4 行に使った conda 環境・
  `PYM3T_DIR`・スクリプトのパスを書くので、起動直後に止まるときはまずここを見る。
- `M3TPoseReceiver` — UDP 5005 で姿勢・品質・K・ArUco 誤差・回転不確か警告を受信。bind 失敗は 1 s ごと再試行、
  ソケットは継承不可（固まった子プロセスがポートを握らないように）、アセンブリ再読み込み前にクローズ。
- `M3TCameraIntrinsics` — トラッカーが送る K / 画像サイズで投影行列を作る（シーンの手書き値は接続前の初期値）。
- `M3TVideoBackground` — TCP 5010 の JPEG 映像を背景に貼る。
- `M3TDemoUI` — デモ用パネル（スクロール可）。Quick Align / Start Demo / 手動微調整（Rotate Z 含む）、
  Marker error 表示と Register marker / Undo last、赤い ROTATION UNCERTAIN バナー。

## ルール

- **Play 中に C# を編集しない**（Preferences → Script Changes While Playing → "Recompile After Finished Playing"）。
- `M3T_*` / `PYM3T_DIR` 以外の環境変数（PATH など）を変えたら Unity Hub ごと再起動。
- シーンの `condaEnvironmentName` は `pym3t` のままコミットする（PC 差は環境変数で吸収）。
