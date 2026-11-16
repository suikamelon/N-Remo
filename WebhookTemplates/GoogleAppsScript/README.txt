N-Remo Google Apps Script 予備テンプレート

1. script.google.com で新しいプロジェクトを作成します。
2. Code.gsの内容を貼り付けます。
3. setupSecrets()内の2つの文字列を長いランダム文字列へ変更し、1回実行します。
4. 「デプロイ」→「新しいデプロイ」→「ウェブアプリ」を選びます。
5. 実行ユーザーを自分、アクセスできるユーザーを全員にしてデプロイします。
6. 発行された末尾が /exec のURLをN-Remoの「中継サービスURL」へ入力します。
7. Webhook受信URLは /exec?webhookKey=WEBHOOK_KEY です。
8. 中継APIキーにはAPI_KEYを入力します。

GAS版は最新200イベントをScript Propertiesに保存する小規模・予備用途です。
大量のイベントや長期運用ではCloudflare Workers + D1を使用してください。
