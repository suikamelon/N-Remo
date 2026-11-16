CREATE TABLE IF NOT EXISTS events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  received_at TEXT NOT NULL,
  provider TEXT NOT NULL,
  device_mac TEXT NOT NULL,
  event_type TEXT NOT NULL,
  context_json TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_events_id ON events(id);
