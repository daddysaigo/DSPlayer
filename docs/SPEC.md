# NewPCRPlayer 仕様書（作業用）

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
| 7 | 安定化・再接続・終了処理・差し替え運用 | **進行中** |

## Phase 7 受け入れ条件

1. 終了時にタイマー / BBS / mpv / 動画クロームが例外なく解放される
2. ライブ切断（end-file error/eof）後、自動再接続を続ける（長めバックオフ）
3. 再接続中はステータスバーに状態が出る
4. 差し替えチェックリストが [PECARECORDER.md](PECARECORDER.md) に揃っている

## Phase 6b 受け入れ条件

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

## 技術スタック

- UI: WPF (.NET 8) + 一部 WinForms（mpv ホスト / 動画上クローム）
- 再生: libmpv（P/Invoke）
- 対象: Windows x64

## 開発対象外

YP / チャンネル一覧 / PeerCastStation / 配信者機能 / OBS / P2P 本体
