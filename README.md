# DSPlayer

PeCaRecorderから起動して使う、Windows向けのPeerCast視聴プレイヤーです。動画再生、掲示板コメント表示・書き込み、コメント画像表示、ウィンドウ吸着などを1つの画面にまとめています。

> 個人開発の非公式ソフトです。PeerCastStation、PeCaRecorder、PCRPlayerの各開発者とは関係ありません。

## 参考にしたソフト

DSPlayerの開発にあたり、[PCRPlayer](http://pecatv.s25.xrea.com/)の機能や使い勝手を参考にしました。PCRPlayerの作者・関係者の皆さまに感謝します。DSPlayerは独立した非公式ソフトです。

## 必要環境

- Windows 10/11 x64
- PeerCastStation
- PeCaRecorder

GitHub Releasesの配布ZIPは.NETランタイムとlibmpvを同梱するため、通常は追加インストール不要です。

## インストール

1. [Releases](https://github.com/daddysaigo/DSPlayer/releases)から最新の`DSPlayer-*-win-x64.zip`をダウンロードします。
2. ZIPを任意のフォルダへ展開します。
3. PeCaRecorderの「オプション → 全般の設定 → プレイヤー」に次を追加します。

| 項目 | 値 |
|---|---|
| パス | 展開した`DSPlayer.exe` |
| 引数 | `"$x" "$0" "$3" "$6"` |
| タイプ | `FLV\|WMV`（必要に応じて`FLV\|UNKNOWN`） |
| ブラウザで開く | オフ |
| IDを検証 | オン推奨 |

チャンネルをPeCaRecorderから開くとDSPlayerが起動します。DSPlayer.exeを直接起動してもチャンネルは再生されません。

### 起動引数

- `$x`: ストリームURL
- `$0`: チャンネル名
- `$3`: 掲示板・Contact URL
- `$6`: PeCaRecorderが取得した視聴者数

`$6`は起動時点の値で、自動更新されません。ビットレートはDSPlayerが受信量を測定するため自動更新されます。

## 主な操作

| 操作 | 動作 |
|---|---|
| マウスホイール / ↑ / ↓ | 音量変更 |
| 音量表示をクリック | ミュート切り替え |
| Space | 一時停止 / 再開 |
| F11 / 動画をダブルクリック | フルスクリーン |
| Esc | 画像プレビュー / フルスクリーンを閉じる |
| 動画中央をドラッグ | ウィンドウ移動・吸着 |
| Shiftを押しながら移動 | 吸着を一時解除 |
| コメント内`>>N` | 該当レスへ移動 |
| コメント内URL | 既定ブラウザで開く |
| 書込 Shift+Enter | 掲示板へ投稿 |
| 右クリック | 設定・再接続・掲示板操作 |

## BBSブラウザ

PCRBrowserなどの外部BBSブラウザは同梱していません。使用する場合はDSPlayerの設定画面で実行ファイルを指定してください。

## 保存場所と通信

- 設定: `%LOCALAPPDATA%\DSPlayer\settings.json`
- ログ: `%LOCALAPPDATA%\DSPlayer\player.log`
- PeerCastStationのローカルHTTP APIへ接続します。
- 掲示板の取得・投稿、およびコメント内画像の表示時に各Webサイトへ接続します。

ログには再生URL、チャンネル名、掲示板URLなどが記録されます。不具合報告へ添付する場合は内容を確認してください。

## 既知の制限

- Windows x64専用です。
- 主にjpnkn掲示板・したらば・5ch系掲示板を対象とし、すべての掲示板には対応しません。
- 視聴者数は起動時点のスナップショットです。
- 配信形式やPeerCastStation側の状態により、再接続が必要になる場合があります。
- コード署名していない配布物ではWindows SmartScreenの警告が表示される場合があります。

## ソースからビルド

必要環境は.NET 8 SDKと7-Zipです。

```powershell
git clone https://github.com/daddysaigo/DSPlayer.git
cd DSPlayer
.\scripts\fetch-libmpv.ps1
dotnet test .\tests\DSPlayer.Tests\DSPlayer.Tests.csproj
dotnet build .\src\DSPlayer\DSPlayer.csproj -c Release
```

自己完結型の配布ZIPを作る場合:

```powershell
.\scripts\publish-release.ps1
```

## ライセンス

DSPlayerは[GNU GPL version 2 or later](LICENSE)で公開します。第三者コンポーネントについては[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)を参照してください。

本ソフトウェアは無保証です。利用によって生じた損害について作者は責任を負いません。
