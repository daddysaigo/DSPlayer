# DSPlayer 仕様書（作業用）

最終更新: 2026-08-12

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

## 実装済み機能（現状サマリ）

### 再生

- libmpv 埋め込み（WinForms `PlayerPanel` + `wid`）
- 起動引数 `"$x" "$0" "$3"`（stream/pls・ch名・Contact BBS）
- `/pls/` → `/stream/` 変換、`tip` 維持
- 音量（ホイール / ↑↓、初期 0）
- 一時停止 Space、終了メニュー
- **ライブ再接続**
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

- **UI テーマ（スキン）**: 設定 `UiTheme` = `Classic` | `Grok`（いつでも切替・即反映）
  - Classic: 従来ダーククローム + 平面コメント
  - Grok: 暖色ペーパーコメントカード + ソフトな書込/ステータス/クローム
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
- 新着ハイライト、最下部追従
- `>>N` / `＞＞N` アンカー（表示中のみジャンプ）
- スレタイクリックで一覧ポップアップ（書込可=黒 / 満了=灰）
- 設定: ヘッダ/本文フォント、レス#・名前・日時の表示、取得間隔、正規化 など
- 設定ファイル: `%LOCALAPPDATA%\DSPlayer\settings.json`
- ログ: `%LOCALAPPDATA%\DSPlayer\player.log`

### ステータスバー

- 左: ch 名・種別・ジャンル等（view.xml）+ **実受信ビットレート**（約 1 秒更新）+ fps  
  - ビットレートは channel.xml 申告値ではなく mpv `packet-*-bitrate` 等
  - 受信なし時は `0kbps`
- 右: 解像度・再生経過、右端に音量

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
| `src/DSPlayer/MainWindow.xaml(.cs)` | メイン UI、FS、再接続、ステータス |
| `src/DSPlayer/Services/Mpv/MpvPlayerHost.cs` | libmpv、キャッシュ、bitrate API |
| `src/DSPlayer/Services/VideoChromeOverlay.cs` | 右上 min/max/close |
| `src/DSPlayer/Services/FsChromeOverlay.cs` | FS 書込/ステータス フロート |
| `src/DSPlayer/Services/Bbs/*` | 掲示板 |
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

## 次にやること（Phase 7 クローズ後）

1. **本番差し替え運用** — [PECARECORDER.md](PECARECORDER.md) に従い PeCaRecorder から利用 / 必要なら `PCRPlayer.exe` 名で置換
2. 実運用で出た不具合の修正（バグ報告ベース）
3. （任意）長時間放置・連続 ch 切替の追加安心確認

## Phase 6b 受け入れ条件（参考・完了）

1. 新着レスがハイライトされ、最下部へ自動スクロールする
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
