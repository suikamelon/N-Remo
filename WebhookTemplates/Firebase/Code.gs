function setup() {
  PropertiesService.getScriptProperties().setProperties({
    FIREBASE_DATABASE_URL: 'https://プロジェクトID-default-rtdb.firebaseio.com',
    FIREBASE_WEB_API_KEY: 'FirebaseのWeb APIキー',
    FIREBASE_EMAIL: 'N-Remo専用ユーザーのメールアドレス',
    FIREBASE_PASSWORD: 'N-Remo専用ユーザーのパスワード',
    WEBHOOK_KEY: 'ここを推測できない長いランダム文字列に変更'
  });
}

function output(value) {
  return ContentService.createTextOutput(JSON.stringify(value))
    .setMimeType(ContentService.MimeType.JSON);
}

function firebaseToken(settings) {
  const response = UrlFetchApp.fetch(
    'https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=' +
      encodeURIComponent(settings.getProperty('FIREBASE_WEB_API_KEY')), {
      method: 'post', contentType: 'application/json',
      payload: JSON.stringify({ email: settings.getProperty('FIREBASE_EMAIL'),
        password: settings.getProperty('FIREBASE_PASSWORD'), returnSecureToken: true }),
      muteHttpExceptions: true
    });
  if (response.getResponseCode() !== 200)
    throw new Error('Firebase authentication failed: ' + response.getContentText());
  return JSON.parse(response.getContentText()).idToken;
}

function doPost(e) {
  const settings = PropertiesService.getScriptProperties();
  if (!e || e.parameter.webhookKey !== settings.getProperty('WEBHOOK_KEY'))
    return output({ error: 'unauthorized' });
  let body;
  try { body = JSON.parse(e.postData.contents || '{}'); }
  catch (_) { return output({ error: 'invalid json' }); }
  const context = body.context || {};
  const deviceMac = context.deviceMac || context.deviceId || '';
  if (!deviceMac) return output({ error: 'device id is missing' });
  const id = Date.now() * 1000 + Math.floor(Math.random() * 1000);
  const event = { id: id, receivedAt: new Date().toISOString(), provider: 'SwitchBot',
    deviceMac: deviceMac, eventType: body.eventType || 'changeReport', context: context };
  const databaseUrl = settings.getProperty('FIREBASE_DATABASE_URL').replace(/\/$/, '');
  const response = UrlFetchApp.fetch(databaseUrl + '/events/' + id + '.json?auth=' +
    encodeURIComponent(firebaseToken(settings)), {
      method: 'put', contentType: 'application/json',
      payload: JSON.stringify(event), muteHttpExceptions: true
    });
  if (response.getResponseCode() < 200 || response.getResponseCode() >= 300)
    return output({ error: 'firebase write failed', status: response.getResponseCode(),
      response: response.getContentText() });
  return output({ ok: true, id: id });
}

function doGet() {
  return output({ ok: true, service: 'N-Remo Firebase Webhook Receiver' });
}
