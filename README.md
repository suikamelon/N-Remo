# N-Remo

日本語 | [English](README.en.md)

N-Remoは、Nature Remo、SwitchBot、Tuyaの機器をWindowsから操作するための非公式WPFアプリケーションです。ボタンを自由に配置し、各社のコマンド、センサー表示、ログ、簡易オートメーションを一つの画面で扱えます。

[アプリのスクリーンショットを見る](screenshots/)

> [!IMPORTANT]
> N-RemoはNature株式会社、SWITCHBOT株式会社、Tuya Inc.による公式アプリではありません。各サービス名、製品名、商標はそれぞれの権利者に帰属します。

## 主な機能

- Nature RemoローカルAPIによる赤外線の取得・送信
- Nature Remo Cloud API、SwitchBot API、Tuya APIのコマンド実行
- 自由に追加・削除・配置できる操作ボタン
- ボタンの表示名、色、サイズ、重なり順、装飾の編集
- Nature Remo、SwitchBot、Tuyaのセンサーデータ表示
- センサーログと期間選択グラフ
- 複数条件・複数コマンドに対応した簡易オートメーション
- Cloudflare WebSocketまたはFirebase Realtime Database＋SSEによるSwitchBot Webhook連携
- グローバルホットキー、マウスジェスチャー、タスクトレイ
- 設定とボタンデータのバックアップ／復元
- 日本語／英語表示

## 動作環境

- Windows 10 バージョン2004（ビルド19041）以降
- x64環境
- インターネット接続（各社クラウドAPIやWebhookを使用する場合）
- 同一LAN接続（Nature RemoローカルAPIを使用する場合）

自己完結型の配布版は、別途.NET Desktop Runtimeをインストールせずに実行できます。

## インストール

1. [Releases](https://github.com/suikamelon/N-Remo/releases)から最新の配布ZIPをダウンロードします。
2. ZIPを任意のフォルダーへ展開します。
3. `N-Remo.exe`を起動します。

配布版に`Language`や`WebhookTemplates`フォルダーが含まれている場合は、`N-Remo.exe`と一緒に保管してください。

未署名の配布版ではWindows SmartScreenが表示される場合があります。ダウンロード元とReleaseに掲載されたSHA-256を確認してください。

## 初期設定

メイン画面を右クリックして「設定」を開き、利用するサービスだけ設定します。

### Nature Remo

- ローカルAPIのみ使用する場合、クラウドアクセストークンは不要です。
- サーバーコマンドやクラウドAPIを使用する場合はアクセストークンが必要です。

### SwitchBot

- SwitchBotアプリで取得したTokenとSecretを入力します。
- Webhook連携を使う場合は、`WebhookTemplates`にある手順書を参照してください。

### Tuya

- Tuya IoT Platformで作成したAccess ID、Access Secret、利用地域のEndpointを入力します。

> [!WARNING]
> 設定のバックアップにはToken、Secret、パスワードなどの認証情報が含まれます。第三者へ渡さず、安全な場所に保管してください。

## Webhookテンプレート

- `WebhookTemplates/Cloudflare`：Cloudflare Workers＋Durable Objects＋WebSocket
- `WebhookTemplates/Firebase`：Google Apps Script＋Firebase Realtime Database＋SSE

各フォルダーの`README.txt`に初心者向けの詳しい設定手順があります。

## ソースからビルド

必要なもの：

- Visual Studio 2026 Community以降、または.NET 10 SDK
- .NETデスクトップ開発ワークロード
- Windows 10 SDK 10.0.19041.0以降

```powershell
dotnet restore N-Remo.csproj
dotnet build N-Remo.csproj -c Release
```

自己完結型の発行例：

```powershell
dotnet publish N-Remo.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## データの保存場所

設定、ボタンデータ、ログなどはユーザー環境へ保存されます。リポジトリへ実データ、`MyData.db`、センサーログ、バックアップ、TokenやSecretをコミットしないでください。

## 不具合・要望

[GitHub Issues](https://github.com/suikamelon/N-Remo/issues)へ登録してください。Token、Secret、Webhook URL、個人情報を含むログやスクリーンショットは投稿しないでください。

セキュリティ上の問題は[SECURITY.md](SECURITY.md)に従って報告してください。

プライバシーについては[PRIVACY.md](PRIVACY.md)を参照してください。

## ライセンス

N-Remoは[MIT License](LICENSE)で公開します。

Copyright (c) 2026 suikamelon

