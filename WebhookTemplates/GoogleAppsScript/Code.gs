function setupSecrets() {
  PropertiesService.getScriptProperties().setProperties({
    API_KEY: 'ここを長いランダム文字列に変更',
    WEBHOOK_KEY: 'ここを別の長いランダム文字列に変更',
    NEXT_ID: '1', EVENTS: '[]'
  });
}

function output(value) {
  return ContentService.createTextOutput(JSON.stringify(value))
    .setMimeType(ContentService.MimeType.JSON);
}

function doPost(e) {
  const p = PropertiesService.getScriptProperties();
  if (!e || e.parameter.webhookKey !== p.getProperty('WEBHOOK_KEY'))
    return output({ error: 'unauthorized' });
  const body = JSON.parse(e.postData.contents || '{}');
  const context = body.context || {};
  const lock = LockService.getScriptLock();
  lock.waitLock(10000);
  try {
    const id = Number(p.getProperty('NEXT_ID') || '1');
    const events = JSON.parse(p.getProperty('EVENTS') || '[]');
    events.push({ id: id, receivedAt: new Date().toISOString(),
      provider: 'SwitchBot', deviceMac: context.deviceMac || context.deviceId || '',
      eventType: body.eventType || 'changeReport', context: context });
    p.setProperty('EVENTS', JSON.stringify(events.slice(-200)));
    p.setProperty('NEXT_ID', String(id + 1));
    return output({ ok: true, id: id });
  } finally { lock.releaseLock(); }
}

function doGet(e) {
  const p = PropertiesService.getScriptProperties();
  if (!e || e.parameter.key !== p.getProperty('API_KEY'))
    return output({ error: 'unauthorized' });
  const path = (e.pathInfo || '').toLowerCase();
  if (path === 'health') return output({ ok: true, service: 'N-Remo Relay', version: 1 });
  if (path === 'events') {
    const after = Number(e.parameter.after || '0');
    const events = JSON.parse(p.getProperty('EVENTS') || '[]').filter(x => x.id > after);
    return output({ latestEventId: events.length ? events[events.length - 1].id : after, events: events });
  }
  return output({ error: 'not found' });
}
