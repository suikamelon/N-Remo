N-Remo Cloudflare WebSocketリアルタイム中継テンプレート
=======================================================

このREADMEは、初めてCloudflare Workersを使う方でも設定できるように、アカウント作成からN-Remoへの登録までを順番に説明します。

■ このテンプレートでできること

SwitchBotから届いたWebhookをCloudflare Workerが受信し、WebSocketでN-Remoへリアルタイム転送します。N-Remoは定期確認（ポーリング）を行いません。一時切断中のイベントは最大1,000件保存され、再接続時に配信されます。SwitchBotのTokenとSecretをCloudflareへ保存する必要はありません。

■ 用意するもの

・Cloudflareの無料アカウント
・Windowsパソコン
・Node.js（LTS版を推奨）
・このCloudflareテンプレートフォルダー
・N-Remoで設定済みのSwitchBot TokenとSecret

■ 手順1：Cloudflareアカウントを準備する

1. Cloudflareのサイトで無料アカウントを作成します。
2. 確認メールを開き、メールアドレスを確認します。
3. 管理画面の「Workers & Pages」を一度開きます。
4. 初回は workers.dev サブドメインを、画面の案内に従って作成します。

メール未確認の場合は「You need to verify your email address ... [code: 10034]」、Workers & Pagesを開いていない場合は「You need a workers.dev subdomain ... [code: 10063]」が表示されることがあります。

■ 手順2：Node.jsをインストールする

1. Node.js公式サイトからLTS版をインストールします。
2. PowerShellを開き、次を1行ずつ実行します。

  node --version
  npm --version

両方ともバージョン番号が表示されれば完了です。「認識されません」と表示された場合はWindowsを再起動して再確認します。

■ 手順3：テンプレートフォルダーでPowerShellを開く

1. エクスプローラーで、このREADMEがあるCloudflareフォルダーを開きます。
2. フォルダー内の何もない場所をShiftキーを押しながら右クリックします。
3. 「ターミナルで開く」を選びます。

フォルダーは後から移動しても構いません。ただし、以降のコマンドは移動後のフォルダーで実行してください。

■ 手順4：必要なツールを準備する

  npm install

完了すると node_modules フォルダーなどが作成されます。赤いエラーがなく入力待ちへ戻れば成功です。

■ 手順5：Cloudflareへログインする

  npx wrangler login

ブラウザーが開いたらCloudflareへログインし、アクセスを許可します。

■ 手順6：2種類の秘密文字列を作る

・API_KEY：N-RemoがWebSocketへ接続するためのパスワード
・WEBHOOK_SECRET：SwitchBotのWebhook受信URLを保護する文字列

2つは別の値にしてください。PowerShellで安全な長い文字列を作る例：

  [guid]::NewGuid().ToString("N") + [guid]::NewGuid().ToString("N")

2回実行し、それぞれを安全な場所へ控えます。

■ 手順7：秘密文字列をCloudflareへ登録する

  npx wrangler secret put API_KEY --name n-remo-relay

入力を求められたらAPI_KEYを貼り付けてEnterを押します。文字が画面に表示されなくても正常です。

  npx wrangler secret put WEBHOOK_SECRET --name n-remo-relay

次はWEBHOOK_SECRETを入力します。Workerを新規作成するか質問された場合は、Yを入力してEnterを押します。

「Required Worker name missing」と出る場合は、末尾の「--name n-remo-relay」を確認してください。

■ 手順8：Cloudflareへ公開する

  npm run deploy

成功すると次のようなURLが表示されます。

  https://n-remo-relay.あなたのサブドメイン.workers.dev

「Uploaded」「Deployed」とURLが表示されれば成功です。workers_devやPreview URLsに関するWARNINGは、今回のworkers.dev公開では問題ありません。

■ 手順9：公開状態を確認する

ブラウザーで「発行されたURL/health」を開きます。

  https://n-remo-relay.example.workers.dev/health

正常な応答が表示されればWorkerは起動しています。

■ 手順10：N-Remoへ入力する

N-Remoの「Webhook連携」タブで次のように設定します。

1. 「Webhook連携を有効にする」をオンにします。
2. 「利用する中継サービス」で「Cloudflare WebSocket」を選びます。
3. 「リアルタイム接続URL」へ発行URLを入力します。末尾の /ws は不要です。
4. 「Webhook受信URL」へ次を入力します。

   発行URL/webhook/WEBHOOK_SECRET

   例：https://n-remo-relay.example.workers.dev/webhook/ここにWEBHOOK_SECRET

5. 「接続キー / Web APIキー」へAPI_KEYを入力します。
6. 「設定を保存」→「接続確認」→「Webhookを登録」の順に押します。
7. 「登録状態を確認」を押し、登録URLが表示されることを確認します。

API_KEYとWEBHOOK_SECRETを逆に入力しないでください。

■ 手順11：動作を確認する

1. N-Remoを起動したままSwitchBotアプリまたは実機を操作します。
2. 「サーバーセンサー」タブで値が更新されるか確認します。
3. 必要に応じて簡易オートメーションの条件へ使用します。

製品や操作方法によってWebhookで通知される項目は異なります。通知されない状態は「今すぐ更新」または自動更新によるAPI取得で補完してください。

■ よくある問題

● 接続確認に失敗する
・URLの前後に空白がなく、https://で始まっているか確認します。
・N-RemoとCloudflareのAPI_KEYが同じか確認します。
・もう一度 npm run deploy を実行します。

● Webhookが届かない
・Webhook URL末尾のWEBHOOK_SECRETを確認します。
・「Webhookを登録」を再実行し、「登録状態を確認」でURLを確認します。

● npmやnpxが認識されない
・Node.jsのLTS版をインストールし、PowerShellまたはWindowsを再起動します。

■ テンプレートを更新する場合

ファイルを新しいものへ入れ替えた後、次を実行します。

  npm install
  npm run deploy

API_KEYとWEBHOOK_SECRETを変更しなければ、通常はsecretの再登録は不要です。

■ セキュリティ上の注意

・API_KEYとWEBHOOK_SECRETを他人へ渡したり、スクリーンショットへ写したりしないでください。
・READMEやwrangler.tomlへ秘密文字列を直接書かないでください。
・漏えいの可能性がある場合は、新しい値をsecret putで登録し直してください。
・N-Remoの設定バックアップには認証情報が含まれるため、安全に保管してください。
