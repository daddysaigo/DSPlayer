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
# 1) libmpv 取得（7-Zip 推奨）
cd "D:\Vive cording\NewPCRPlayer"
.\scripts\fetch-libmpv.ps1

# 失敗時は手動:
# https://sourceforge.net/projects/mpv-player-windows/files/libmpv/
# から x86_64 の dev アーカイブを取得し libmpv-2.dll を lib\ へ

# 2) ビルド
dotnet build .\src\NewPCRPlayer\NewPCRPlayer.csproj -c Release

# 3) 起動（本番相当: $x $0 $3）
dotnet run --project .\src\NewPCRPlayer\NewPCRPlayer.csproj -c Release -- `
  "http://localhost:7144/pls/<ChannelID>?tip=host:port" `
  "テストch" `
  "https://example.com/test/read.cgi/board/1/"
```

## PeCaRecorder への登録

| 項目 | 値 |
|------|-----|
| プレイヤー名 | NewPCRPlayer |
| タイプ | `FLV\|WMV` または `FLV\|UNKNOWN` |
| パス | `...\NewPCRPlayer.exe` |
| 引数 | **`"$x" "$0" "$3"`** |

| プレースホルダ | 意味 |
|----------------|------|
| `$x` | ストリーム / プレイリスト URL |
| `$0` | チャンネル名 |
| `$3` | Contact URL（掲示板）※コメントに必要 |

詳細な差し替え手順・チェックリストは  
[docs/PECARECORDER.md](docs/PECARECORDER.md) を参照。

最終的には exe を `PCRPlayer.exe` にリネームして差し替える想定です。

## 操作

| 操作 | 動作 |
|------|------|
| マウスホイール | 音量 ±5 |
| Space | 一時停止 / 再開 |
| ↑ / ↓ | 音量 |
| F11 / ダブルクリック | フルスクリーン |
| Esc | フルスクリーン解除 |
| Ctrl+C | コメント欄 表示/非表示（選択中はコピー） |
| 境界ドラッグ | 動画/コメント幅 |
| 右クリック | メニュー（設定・掲示板を開く等） |
| 書込バー Enter | レス書き込み（2ch系 / したらば） |

設定は `%LOCALAPPDATA%\NewPCRPlayer\settings.json` に保存されます。

## 開発フェーズ

詳細は [docs/SPEC.md](docs/SPEC.md) を参照。

1. ~~調査~~
2. ~~最小プレイヤー~~
3. ~~掲示板ビューア~~
4. ~~PCRPlayer 風 UI~~
5. **起動互換の完成（進行中）**
6. 設定・BBS体感 / コメントUI 強化
