# DSPlayer 仕様書（作業用）

最終更新: 2026-08-23

## 0. 最重要前提

これは PeerCast クライアントの新規開発ではない。
既存の PeerCast 環境に差し込める **PCRPlayer の代替プレイヤー**だけを作る。

```
配信者 → OBS → PeerCastStation → YP → PeCaRecorder → PCRPlayer
                                                      ↑ ここだけ置換
```

- PeCaRecorder / PeerCastStation / YP は変更しない
- 新プレイヤーは「PeCaRecorder から起動される外部プレイヤー」
- 最終目標: `PCRPlayer.exe` を新プレイヤーにリネームして置き換えるだけで動く

## Phase 状態

| Phase | 内容 | 状態 |
|-------|------|------|
| 1 | 連携調査 | 完了 |
| 2 | 最小プレイヤー（URL + libmpv + 音量） | 完了 |
| 3 | 掲示板（Contact URL / コメント） | 完了 |
| 4 | PCRPlayer 風 UI（書込・情報バー・設定） | 完了 |
| 5 | PeCaRecorder 起動互換 | 完了 |
| 6a | board 設定・正規化・URL rewrite・配置 | 完了 |
| 6b | コメント UI（新着・下端追従・フォント・**>>N**） | 完了 |
| 6c | 窓クローム（動画右上 min/max/close オーバーレイ） | 完了 |
| 7 | 安定化・再接続・終了処理・差し替え運用 | **完了** |
| 8 | 勢いメーター・コメント追従・本文リンク/画像 | **完了** |

## Phase 7 受け入れ条件

| # | 条件 | 状態 |
|---|------|------|
| 1 | 終了時にタイマー / BBS / mpv / 動画クロームが例外なく解放される | 完了 |
| 2 | ライブ切断後、自動再接続を続ける（backoff / stall 監視） | 完了 |
| 3 | 再接続中はステータスバーに状態が出る | 完了 |
| 4 | 差し替えチェックリストが [PECARECORDER.md](PECARECORDER.md) に揃っている | 完了 |
| 5 | FS 書込/ステータスがフロート（動画サイズ不変）、切替のびよん抑制 | 完了 |
| 6 | ステータス kbps が実受信（約1秒） | 完了 |

**Phase 7 クローズ:** 2026-08-12  
**Phase 8 クローズ:** 2026-08-23

## Phase 8 受け入れ条件

| # | 条件 | 状態 |
|---|------|------|
| 1 | ステータスに直近 5 分の勢いが出る。最低帯の表記は「過疎」 | 完了 |
| 2 | コメント最下部にいるときだけ新着で追従する。遡っているときは位置を動かさない | 完了 |
| 3 | 遡り中に新着が来たら「新着 N ↓」。クリックまたは最下部へ戻ると追従再開 | 完了 |
| 4 | 本文の `http(s)://` / `ttp(s)://` がリンクになり、クリックで既定ブラウザが開く | 完了 |
| 5 | 画像 URL はコメント幅に収めて表示。クリックで枠なしプレビュー | 完了 |
| 6 | 透過・差分フレームの GIF は合成して同じサイズで再生し、スクロールが跳ねない | 完了 |

## 実装済み機能（現状サマリ）

### 再生

- libmpv 埋め込み（WinForms `PlayerPanel` + `wid`）
- 起動引数 `"$x" "$0" "$3"`（stream/pls・ch名・Contact BBS）
- `/pls/` → `/stream/` 変換、`tip` 維持
- 音量（ホイール / ↑↓、初期 0）
- 一時停止 Space、終了メニュー
- **ライブ再接続**
  - 右クリック →「再接続」で手動再接続。自動再接続の待機中も短い待機からやり直す
  - `end-file`（quit 以外）→ 再接続スケジュール
  - 停滞監視（約 8 秒、受信ビットレート / time-pos が進まない）→ 強制再接続
  - lavf 内部 reconnect は使わず、アプリ側で `loadfile`
  - ステータス: `切断 — 再接続…` / `再接続待機 Xs…` / `再接続中… (N)`
- mpv ライブ向けキャッシュ（メモリ抑制）
  - `demuxer-max-bytes=8MiB`
  - `demuxer-max-back-bytes=0`
  - `demuxer-readahead-secs=1.5`
  - `demuxer-seekable-cache=no`
  - `demuxer-donate-buffer=no`
  - `network-timeout=8`、`keep-open=no`

### UI / 窓

- **UI テーマ（スキン）**: 設定 `UiTheme`（いつでも切替・即反映）
  - Classic / Grok / Neon / Sakura / Terminal / Ember
  - コメント見た目 `CommentListTheme` は独立: Classic / Grok / Neon / Sakura / Sticky / Bubble / Terminal
- 枠なしウィンドウ、動画縁でのリサイズ・ドラッグ
- コメント列スプリッタ、表示 ON/OFF
- **動画右上クローム**（`VideoChromeOverlay`）: 所有 WinForms、ホバーで min/max/close
- **フルスクリーン**
  - F11 / ダブルクリック / Esc
  - 書込欄 + ステータスは **フロート**（`FsChromeOverlay` + ElementHost）
  - レイアウト行高 0 のため show/hide で動画サイズが変わらない
  - 下端ホバーで表示、マウスアウトで即非表示（フォーカスでピン留めしない）
  - 最大化**前**にバーをレイアウトから外す（FS 突入時のサイズポップ軽減）
- 書込欄: Enter=改行で下方向に窓を伸ばす（ContentRow ピクセル固定）、Shift+Enter=送信

### コメント / BBS

- Contact URL からスレ取得・ポーリング
- 仮想化 ListBox、表示は最新 **120** 件（裏ではスレ全体を保持）
- 新着ハイライト
- **追従**
  - 最下部付近にいるときだけ新着で最下部へスクロール
  - 遡って読んでいるときは位置固定。下に「新着 N ↓」（クリックで最新へ＋追従再開）
  - 自分の書込成功・スレ切替・初回読み込みは最下部へ
- `>>N` / `＞＞N` アンカー（表示中のみジャンプ）
- 本文 URL: `http(s)://` と 2ch 風 `ttp(s)://` をリンク化。クリックで既定ブラウザ
- **画像埋め込み**（設定 `EmbedCommentImages`、既定 ON）
  - jpg / png / gif / bmp、Twitter `pbs.twimg.com`（`format=` なしも含む）
  - コメント列幅に収め、高さ最大 240px。同時取得 2 件、1 枚 1.5MB、キャッシュ 32
  - GIF は論理画面に合成（差分フレーム・透過・disposal）。全フレーム同一サイズ
  - 重い GIF（目安 1.2MB / 48 フレーム超）は先頭フレームのみ
  - WebP はリンクのみ（WPF がデコードできない）
- **画像プレビュー**（別ウィンドウ）
  - 枠・タイトルバーなし。押しっぱなしで移動
  - クリック / アクティブ時 Esc / 右クリックで閉じる
  - 端リサイズは画像の縦横比を維持（`WM_SIZING`）
  - 映像 HWND の airspace を避けるためオーバーレイではなく独立ウィンドウ
- スレタイクリックで一覧ポップアップ（書込可=黒 / 満了=灰）
- 設定: ヘッダ/本文フォント、レス#・名前・日時、取得間隔、正規化、勢い見た目、画像埋め込み
- 設定ファイル: `%LOCALAPPDATA%\DSPlayer\settings.json`
- ログ: `%LOCALAPPDATA%\DSPlayer\player.log`

### ステータスバー

- 左: ch 名・種別・ジャンル等（view.xml）+ **実受信ビットレート**（約 1 秒更新）+ fps  
  - ビットレートは channel.xml 申告値ではなく mpv `packet-*-bitrate` 等
  - 受信なし時は `0kbps`
- 右: **勢い**・解像度・再生経過、右端に音量
- **勢い**（直近 5 分のレス数 → 0–100）
  - 既定ヒート: `勢い(n) ▰▰▰▱▱ 過疎|ゆったり|平常運転|にぎやか|超過密`
  - シンプル: `勢い n　過疎|落ち着いてる|普通|活発|熱い`
  - 最低帯（スコア 0–34）の表記は **過疎**
  - 設定 `MomentumStyle` = `Heat` | `Simple`

## 技術スタック

| 層 | 技術 |
|----|------|
| UI | WPF (.NET 8) |
| 動画 HWND | WinForms host + libmpv |
| 動画上 UI | 所有 WinForms ツール窓（airspace 回避） |
| BBS | HttpClient + DAT/HTML パーサ |
| 対象 | Windows x64 |

### 主要ソース

| パス | 役割 |
|------|------|
| `src/DSPlayer/MainWindow.xaml(.cs)` | メイン UI、FS、再接続、ステータス、コメント追従 |
| `src/DSPlayer/Services/Mpv/MpvPlayerHost.cs` | libmpv、キャッシュ、bitrate API |
| `src/DSPlayer/Services/VideoChromeOverlay.cs` | 右上 min/max/close |
| `src/DSPlayer/Services/FsChromeOverlay.cs` | FS 書込/ステータス フロート |
| `src/DSPlayer/Services/Bbs/*` | 掲示板、勢い、本文パーサ |
| `src/DSPlayer/Services/CommentImageLoader.cs` | 画像取得・GIF 合成 |
| `src/DSPlayer/Controls/CommentImageWindow.cs` | 枠なし画像プレビュー |
| `src/DSPlayer/Services/PeerCast/*` | view.xml |
| `docs/PECARECORDER.md` | 差し替え手順・チェックリスト |

### ビルド成果物

```
src/DSPlayer/bin/Release/net8.0-windows/DSPlayer.exe   # 運用確認用
src/DSPlayer/bin/Debug/net8.0-windows/DSPlayer.exe     # 開発用
```

## メモリ（既知の挙動）

| 項目 | 内容 |
|------|------|
| 本家 PCRPlayer | おおよそ 50〜60MB でほぼ一定（ネイティブ） |
| DSPlayer | 起動 ~140MB 前後。.NET + WPF + libmpv の固定費が大きい |
| 再生中 | ワーキングセットが伸び、ある程度で緩やか／頭打ちしやすい（例: 数分で 250〜270MB 付近） |
| 対策済み | demuxer 前方 8MiB、後方 0、seekable cache off |
| 未達 | 本家並み 50MB 台は現実的ではない。無限増が続く場合は別途調査 |

## 既知の差分・後回し（完了後も残るもの）

| 項目 | 状態 |
|------|------|
| FS 切替 | 実用レベルまで抑制済み（DWM/mpv 由来の微差は許容） |
| メモリを本家同等（50MB）まで削減 | **非目標**（.NET+WPF+mpv）。ピーク抑制のみ実施 |
| メモリ無限増が続く場合 | 出たら調査（BBS 全件保持など） |

## 次にやること（Phase 8 クローズ後）

1. **本番差し替え運用** — [PECARECORDER.md](PECARECORDER.md) に従い PeCaRecorder から利用 / 必要なら `PCRPlayer.exe` 名で置換
2. 実運用で出た不具合の修正（バグ報告ベース）
3. （任意）長時間放置・連続 ch 切替の追加安心確認
4. （任意）WebP 埋め込み、重い GIF の再生上限の見直し

## Phase 6b 受け入れ条件（参考・完了）

1. 新着レスがハイライトされ、**最下部にいるとき**最下部へ自動スクロールする（遡り中は固定＋新着ジャンプ）
2. コメント表示フォント（レス情報行 / 本文）を設定できる
3. レス情報行の 番号・名前・日時 を個別に表示 ON/OFF できる
4. 本文中の `>>N` / `＞＞N` クリックで表示中リスト内の該当レスへジャンプする
5. 表示範囲外の N のときはステータス等で分かる

## PeCaRecorder 起動

**正規登録:**

```
引数: "$x" "$0" "$3"
タイプ: FLV|WMV
```

| プレースホルダ | 意味 |
|----------------|------|
| `$x` | ストリーム / プレイリスト URL |
| `$0` | チャンネル名 |
| `$3` | Contact URL（掲示板） |

プレイヤーは `/pls/` を `/stream/` に変換して再生（`tip` クエリは維持）。

詳細: [PECARECORDER.md](PECARECORDER.md)

## 開発対象外

YP / チャンネル一覧 / PeerCastStation / 配信者機能 / OBS / P2P 本体
