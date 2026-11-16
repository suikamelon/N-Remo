N-Remo Firebase Realtime Database＋SSEテンプレート
==================================================

このREADMEは、Firebaseを初めて使う方でも設定できるように、プロジェクト作成からN-Remoへの登録までを順番に説明します。

■ このテンプレートでできること

SwitchBotのWebhookをGoogle Apps Script（GAS）が受信し、Firebase Realtime Databaseへ保存します。N-RemoはFirebaseをSSEで常時購読し、保存された変更をリアルタイムに受信します。数秒ごとの定期確認は行いません。

このテンプレートはFirebase Authenticationの「メールアドレス／パスワード認証」を使用します。匿名認証ではありません。

■ 用意するもの

・Googleアカウント
・Firebaseの無料プロジェクト
・N-Remo専用に使用するメールアドレスとパスワード
・このFirebaseテンプレートフォルダー
・N-Remoで設定済みのSwitchBot TokenとSecret

■ 全体の流れ

1. Firebaseプロジェクトを作成
2. Realtime Databaseを作成
3. メール／パスワード認証を有効化
4. N-Remo専用ユーザーを作成
5. Databaseルールを設定
6. Apps Scriptを作成してWebhook受信処理を公開
7. N-Remoへ各項目を入力
8. SwitchBotへWebhookを登録

■ 手順1：Firebaseプロジェクトを作成する

1. Firebase Consoleを開きます。
2. 「プロジェクトを追加」を押します。
3. 分かりやすい名前（例：N-Remo）を入力します。
4. Googleアナリティクスは、この用途では必須ではありません。
5. 「プロジェクトを作成」を押し、完了するまで待ちます。

■ 手順2：Realtime Databaseを作成する

1. Firebase Console左側の「構築」から「Realtime Database」を開きます。
2. 「データベースを作成」を押します。
3. 利用地域を選択します。通常は近い地域を選びます。
4. セキュリティルールの選択画面では、後で専用ルールへ変更するため、内容を確認して作成します。
5. 作成後、画面上部などに表示されるDatabase URLを控えます。

Database URLの例：

  https://あなたのプロジェクト-default-rtdb.firebaseio.com

末尾まで正確に控えてください。

■ 手順3：メール／パスワード認証を有効にする

1. Firebase Console左側の「構築」から「Authentication」を開きます。
2. 初回は「始める」を押します。
3. 「Sign-in method」または「ログイン方法」を開きます。
4. 「メール／パスワード」を選択します。
5. 「有効にする」をオンにして保存します。

「メールリンク（パスワードなしログイン）」はオンにする必要がありません。

■ 手順4：N-Remo専用ユーザーを作成する

1. Authenticationの「Users」または「ユーザー」タブを開きます。
2. 「ユーザーを追加」を押します。
3. N-Remo専用のメールアドレスと、十分に長いパスワードを入力します。
4. 作成後、一覧に表示される「ユーザーUID」を控えます。

次の3項目を安全な場所へ控えてください。

・メールアドレス
・パスワード
・ユーザーUID

ユーザーUIDはメールアドレスとは異なる長い文字列です。

■ 手順5：Realtime Databaseのルールを設定する

1. このフォルダーの database.rules.json をメモ帳などで開きます。
2. ファイル内にあるUIDの記入箇所2か所を、手順4で控えたユーザーUIDへ置き換えます。
3. Firebase ConsoleでRealtime Databaseを開き、「ルール」タブを選びます。
4. 編集したルールの内容を貼り付けます。
5. 「公開」を押します。

UIDは2か所とも同じ値にしてください。引用符を削除しないでください。
このルールにより、N-Remo専用ユーザー以外からの読み書きを防ぎます。

■ 手順6：Firebase Web APIキーを確認する

1. Firebase Console左上の歯車から「プロジェクトの設定」を開きます。
2. 「全般」タブを開きます。
3. 「ウェブAPIキー」を控えます。

Web APIキーは、N-RemoとApps Scriptの両方で使用します。

■ 手順7：Google Apps Scriptを作成する

1. Google Apps Scriptのサイトを開き、「新しいプロジェクト」を作成します。
2. プロジェクト名を「N-Remo Webhook」などへ変更します。
3. 最初からある Code.gs の内容をすべて削除します。
4. このFirebaseフォルダーにある Code.gs の内容をコピーして貼り付けます。

■ 手順8：appsscript.jsonを設定する

1. Apps Script左側の「プロジェクトの設定」（歯車）を開きます。
2. 「マニフェスト ファイル appsscript.json をエディタで表示する」をオンにします。
3. エディターへ戻り、appsscript.jsonを開きます。
4. このフォルダーのappsscript.jsonの内容へ置き換えて保存します。

■ 手順9：Code.gsのsetup()を設定する

Code.gs内のsetup()で、次の値を自分の環境に合わせて入力します。

・FIREBASE_DATABASE_URL：手順2のDatabase URL
・FIREBASE_WEB_API_KEY：手順6のウェブAPIキー
・FIREBASE_EMAIL：手順4の専用ユーザーのメールアドレス
・FIREBASE_PASSWORD：手順4の専用ユーザーのパスワード
・WEBHOOK_KEY：自分で作成する推測困難な長い文字列

WEBHOOK_KEYはWebhook受信URLを保護します。PowerShellで作成する例：

  [guid]::NewGuid().ToString("N") + [guid]::NewGuid().ToString("N")

入力値の前後にある引用符を削除しないでください。

■ 手順10：setup()を1回実行する

1. Apps Script上部の関数選択欄で「setup」を選びます。
2. 「実行」を押します。
3. 初回はGoogleアカウントの権限確認が表示されます。
4. 内容を確認し、使用するGoogleアカウントで許可します。
5. 実行ログでエラーが出ていないことを確認します。

setup()は設定値をApps Scriptのプロパティへ保存する処理です。値を変更した場合は、もう一度実行してください。

■ 手順11：Apps Scriptをウェブアプリとして公開する

1. 右上の「デプロイ」→「新しいデプロイ」を押します。
2. 種類で「ウェブアプリ」を選びます。
3. 「次のユーザーとして実行」は「自分」を選びます。
4. 「アクセスできるユーザー」は、Webhookを受信できる「全員」を選びます。
5. 「デプロイ」を押します。
6. 表示されたウェブアプリURLを控えます。

URLは必ず /exec で終わる公開用URLを使用します。/dev で終わるテストURLはN-Remoへ登録しません。

■ 手順12：Webhook受信URLを作る

ウェブアプリURLの末尾へ、次を追加します。

  ?webhookKey=手順9のWEBHOOK_KEY

完成例：

  https://script.google.com/macros/s/文字列/exec?webhookKey=ここにWEBHOOK_KEY

このURL全体がN-Remoの「Webhook受信URL」です。

■ 手順13：N-Remoへ入力する

N-Remoの「Webhook連携」タブで次のように設定します。

1. 「Webhook連携を有効にする」をオンにします。
2. 「利用する中継サービス」でFirebaseを選びます。
3. 「リアルタイム接続URL」へFirebase Database URLを入力します。
4. 「Webhook受信URL」へ手順12で作ったGASのURLを入力します。
5. 「接続キー / Web APIキー」へFirebase Web APIキーを入力します。
6. 「Firebase専用メール」へ専用ユーザーのメールアドレスを入力します。
7. 「Firebase専用パスワード」へ専用ユーザーのパスワードを入力します。
8. 「設定を保存」→「接続確認」→「Webhookを登録」の順に押します。
9. 「登録状態を確認」で登録URLを確認します。

■ 手順14：動作を確認する

1. N-Remoを起動したままSwitchBotアプリまたは実機を操作します。
2. 「サーバーセンサー」タブで値が更新されるか確認します。
3. 必要に応じて簡易オートメーションの条件へ使用します。

■ Apps Scriptのコードを更新した場合

コードを保存するだけでは、公開中のウェブアプリへ反映されないことがあります。

1. 「デプロイ」→「デプロイを管理」を開きます。
2. 編集（鉛筆）を押します。
3. バージョンで「新バージョン」を選びます。
4. 「デプロイ」を押します。

同じデプロイを更新すれば、通常は /exec URLを変更せずに利用できます。

■ よくある問題

● 接続確認に失敗する
・Database URL、Web APIキー、専用メール、パスワードを確認します。
・Authenticationでメール／パスワード認証が有効か確認します。
・専用ユーザーが削除されていないか確認します。

● Webhook登録後も値が届かない
・GAS URLが /exec で終わる部分を使用しているか確認します。
・URL末尾のwebhookKeyがsetup()のWEBHOOK_KEYと同じか確認します。
・Apps Scriptの「実行数」でdoPostのエラーを確認します。

● Permission deniedが出る
・database.rules.jsonのUIDが専用ユーザーのUIDと一致するか確認します。
・ルール内の2か所を両方変更したか確認します。

● Apps Scriptを修正しても動作が変わらない
・「デプロイを管理」で新バージョンを公開します。

■ セキュリティ上の注意

・Databaseを誰でも読み書きできるルールのまま使用しないでください。
・専用メール、パスワード、Web APIキー、WEBHOOK_KEYを公開しないでください。
・GASのWebhook URLをスクリーンショットへ写さないでください。
・N-Remoの設定バックアップには認証情報が含まれるため、安全に保管してください。
・認証情報が漏れた場合は、専用パスワードやWEBHOOK_KEYを変更してください。

■ データについて

FirebaseにはWebhookイベントが保存されます。長期間使用してデータ量が増えた場合は、Firebase Consoleで使用量を確認してください。N-Remo側のセンサーログ保存とは別のデータです。
