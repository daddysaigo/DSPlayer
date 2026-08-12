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
| 5 | PeCaRecorder 起動互換の完成 | 完了 |
| 6a | board設定（interval/UA/正規化/URL rewrite/placement） | **完了** |
| 6b | コメントUI（chat風・新着・>>N） | 未着手 |
| 6 | フルスクリーン・設定・リーク対策 | 一部先行（FS/音量/起動音量0） |

## PeCaRecorder 起動

オプション → 全般の設定 → プレイヤー

**正規登録（元 PCRPlayer と同じ）:**

```
引数: "$x" "$0" "$3"
タイプ: FLV|WMV
```

| プレースホルダ | 意味 | 例 |
|----------------|------|-----|
| `$x` | ストリーム / プレイリスト URL | `/stream/` または `/pls/`（実機は `/pls/<id>?tip=host:port` が多い） |
| `$0` | チャンネル名 | `木寺 (FLV)` |
| `$3` | Contact URL（掲示板） | `https://.../read.cgi/...` |
| `$8` | ビットレート（未使用可） | `2947` |
| `$9` | タイプ（未使用可） | `FLV` |

実測 PeCaRecorder 起動例:

```
NewPCRPlayer.exe
  "http://127.0.0.1:7144/pls/<ChannelID>?tip=host:port"
  "チャンネル名"
  "https://.../read.cgi/..."
```

### 引数パース方針（Phase 5）

1. **位置優先**: 第1引数が PeerCast media なら  
   `[$x] [$0?] [$3?]` として解釈  
   - 第3引数の HTTP URL は BBS キーワードが無くても Contact とする  
     （`$3` 互換）
2. **ヒューリスティック**: 上記以外は URL 種別で振り分け
3. プレイヤーは `/pls/` を `/stream/` に変換して再生（`tip` クエリは維持）

差し替え手順・チェックリスト: [PECARECORDER.md](PECARECORDER.md)

## 技術スタック（確定）

- UI: WPF (.NET 8)
- 再生: libmpv（自前 P/Invoke 薄いラッパー）
- 言語: C#
- 対象: Windows x64

## Phase 2 受け入れ条件

1. `NewPCRPlayer.exe "http://localhost:7144/stream/...." "ch名"` で起動
2. libmpv で当該 URL を再生しウィンドウに映像が出る
3. マウスホイールで音量調整

## Phase 5 受け入れ条件

1. 引数 `"$x" "$0" "$3"` で media / チャンネル名 / Contact が取れる
2. `/pls/?tip=` → `/stream/?tip=` 変換
3. ドキュメントに PeCaRecorder 登録と PCRPlayer.exe 差し替え手順がある

## 開発対象外

YP / チャンネル一覧 / PeerCastStation / 配信者機能 / OBS / P2P 本体
