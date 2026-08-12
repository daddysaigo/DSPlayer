# NewPCRPlayer

既存 PeerCast 視聴環境向けの **PCRPlayer 代替プレイヤー**。

```
PeCaRecorder → NewPCRPlayer.exe "$x" "$0" "$3"
```

PeerCastStation / YP / PeCaRecorder は変更しません。

## 必要環境

- Windows 10/11 **x64**
- .NET 8 Desktop Runtime（開発時は SDK 8）
- libmpv（`libmpv-2.dll` または `mpv-1.dll`）

## セットアップ

```powershell
cd "D:\Vive cording\NewPCRPlayer"
.\scripts\fetch-libmpv.ps1
dotnet build .\src\NewPCRPlayer\NewPCRPlayer.csproj -c Release

dotnet run --project .\src\NewPCRPlayer\NewPCRPlayer.csproj -c Release -- `
  "http://localhost:7144/pls/<ChannelID>?tip=host:port" `
  "テストch" `
  "https://example.com/test/read.cgi/board/1/"
```

## PeCaRecorder への登録

| 項目 | 値 |
|------|-----|
| パス | `...\NewPCRPlayer.exe` |
| 引数 | **`"$x" "$0" "$3"`** |
| タイプ | `FLV\|WMV` |

詳細は [docs/PECARECORDER.md](docs/PECARECORDER.md)。

## 操作

| 操作 | 動作 |
|------|------|
| マウスホイール | 音量 ±5 |
| Space | 一時停止 / 再開 |
| ↑ / ↓ | 音量 |
| F11 / ダブルクリック | フルスクリーン |
| Esc | フルスクリーン解除 |
| 動画右上ホバー | 最小化・最大化・閉じる |
| 境界ドラッグ | 動画/コメント幅 |
| コメント内 `>>N` | 表示中の該当レスへジャンプ |
| スレタイクリック | スレッド一覧 |
| 右クリック | メニュー（設定・掲示板など） |
| 書込 Shift+Enter | レス書き込み |
| 書込 Enter | 改行（欄が下に伸びる） |
| フルスクリーン下端ホバー | 書込欄・ステータスをフロート表示（動画サイズは不変） |

設定: `%LOCALAPPDATA%\NewPCRPlayer\settings.json`  
ログ: `%LOCALAPPDATA%\NewPCRPlayer\player.log`

## 開発フェーズ

詳細は [docs/SPEC.md](docs/SPEC.md)。

1. ~~調査~~ 〜 6. ~~コメント UI / 窓クローム~~
7. ~~安定化・再接続・終了処理・差し替え運用~~ **完了**（2026-08-12）

### 次のステップ

- [docs/PECARECORDER.md](docs/PECARECORDER.md) に従い **本番差し替え・運用**
- 不具合は実運用ベースで対応
