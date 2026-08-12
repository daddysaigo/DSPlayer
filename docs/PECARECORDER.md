# PeCaRecorder 連携・差し替え手順

NewPCRPlayer を **PCRPlayer の代替**として使うための手順です。  
PeerCastStation / YP / PeCaRecorder 本体の改変は不要です。

```
配信者 → OBS → PeerCastStation → YP → PeCaRecorder → NewPCRPlayer
                                                      ↑ ここだけ置換
```

## 1. 推奨: プレイヤーとして追加登録

PeCaRecorder → **オプション → 全般の設定 → プレイヤー**

| 項目 | 値 |
|------|-----|
| 有効 | チェック |
| パス | `D:\...\NewPCRPlayer\src\NewPCRPlayer\bin\Release\net8.0-windows\NewPCRPlayer.exe`（ビルド成果物） |
| 引数 | `"$x" "$0" "$3"` |
| タイプ | `FLV\|WMV` または `FLV\|UNKNOWN` |
| ブラウザで開く | オフ |
| 接続してから起動 | オフ（任意） |
| IDを検証 | オン推奨 |

### プレースホルダ（元 PCRPlayer と同じ）

| 記号 | 意味 | 例 |
|------|------|-----|
| `$x` | ストリーム / プレイリスト URL | `http://127.0.0.1:7144/pls/<ID>?tip=host:port` |
| `$0` | チャンネル名 | `木寺 (FLV)` |
| `$3` | Contact URL（掲示板） | `https://.../read.cgi/...` |

**必ず `$3` を含めてください。**  
これが無いとコメント欄の Contact 解決が PeerCast API 頼みになり、板が開かないことがあります。

元の `PeCaRecorder.xml` 登録例:

```xml
<item enable="true"
      path="PCRPlayer\PCRPlayer.exe"
      arg="&quot;$x&quot; &quot;$0&quot; &quot;$3&quot;"
      type="WMV|FLV"
      browser="false" connect="false" verify="true"/>
```

### 起動後の内部変換

| 入力 | 再生 URL |
|------|----------|
| `.../pls/<id>?tip=...` | `.../stream/<id>?tip=...`（`tip` は維持） |
| `.../stream/<id>...` | そのまま |

## 2. 差し替え運用（PCRPlayer.exe 名で置く）

最終的には **ファイル名を `PCRPlayer.exe` にして** 既存パスを上書きできます。

### 手順

1. 既存の PCRPlayer フォルダをバックアップ  
   例: `...\PeCaRecorder_v091\PCRPlayer` → `PCRPlayer.bak`
2. Release ビルド:

   ```powershell
   cd "D:\Vive cording\NewPCRPlayer"
   .\scripts\fetch-libmpv.ps1   # 未取得時
   dotnet build .\src\NewPCRPlayer\NewPCRPlayer.csproj -c Release
   ```

3. 出力フォルダの中身を用意:

   ```
   NewPCRPlayer.exe   →  PCRPlayer.exe にリネーム
   lib\mpv 系 DLL     →  同じ階層の lib\ または exe 横（ビルド設定に従う）
   ```

4. 配布物を PCRPlayer フォルダへコピー（**上書き前にバックアップ必須**）
5. PeCaRecorder のプレイヤー設定パスが  
   `PCRPlayer\PCRPlayer.exe` のままなら **引数だけ**  
   `"$x" "$0" "$3"` になっているか確認
6. チャンネルを再生して、映像・コメント・書き込みを確認

### 注意

- NewPCRPlayer は **x64** 専用（`PCRPlayer64.exe` 相当）
- 32bit のみの環境は非対応
- 設定は exe 横の XML ではなく  
  `%LOCALAPPDATA%\NewPCRPlayer\settings.json`
- 本格 BBS UI は引き続き同梱の **PCRBrowser.exe** を起動可能  
  （右クリック → BBSブラウザ）

### 同梱したいもの（任意）

| 元フォルダ | 用途 |
|------------|------|
| `PCRBrowser.exe` | 本格掲示板ブラウザ |
| `skin\` | PCRBrowser 用（NewPCRPlayer 本体は未使用） |

## 3. 手動起動テスト

```powershell
# ストリームのみ
dotnet run --project .\src\NewPCRPlayer\NewPCRPlayer.csproj -c Release -- `
  "http://127.0.0.1:7144/stream/<ChannelID>" "テストch"

# 本番相当（$x $0 $3）
dotnet run --project .\src\NewPCRPlayer\NewPCRPlayer.csproj -c Release -- `
  "http://127.0.0.1:7144/pls/<ChannelID>?tip=host:port" `
  "チャンネル名" `
  "https://bbs.example/test/read.cgi/board/1234567890/"
```

## 4. 受け入れチェックリスト（Phase 5）

- [ ] PeCaRecorder からダブルクリック再生で NewPCRPlayer が起動する
- [ ] `/pls/?tip=` が映像として再生される
- [ ] ウィンドウタイトル / 情報バーにチャンネル名が出る
- [ ] `$3` の Contact でコメントが流れる（または板トップから最新スレ解決）
- [ ] PCRBrowser 連携（BBS ボタン）が動く
- [ ] （差し替え時）`PCRPlayer.exe` 名でも起動する

## 5. トラブルシュート

| 症状 | 確認 |
|------|------|
| 黒画面 | PeerCastStation が受信中か、`/stream/` でブラウザ/curl できるか |
| コメント無し | 引数に `$3` があるか、Contact が BBS URL か |
| libmpv エラー | `lib\mpv-2.dll`（または `mpv-1.dll`）が出力先にあるか |
| 起動しない | x64 OS か、.NET 8 Desktop Runtime が入っているか |
