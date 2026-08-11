-- Migration 004: the operation journal — ARCHITECTURE.md §19.2.
-- Trimmed from the full §22 schema to what M6 actually needs: the
-- AI-plan/session-linkage columns (origin, session_id, plan_json,
-- undo_expires_utc) are meaningful once the Agent (M8+) exists to produce
-- them; they're straightforward additive columns for a later migration,
-- not a redesign, so they're deliberately left out until something actually
-- populates them.

CREATE TABLE operations (
  id                 TEXT PRIMARY KEY,   -- UUIDv7 (time-ordered), Guid.CreateVersion7()
  kind               TEXT NOT NULL,      -- move|copy|rename|delete|create|undo
  status             TEXT NOT NULL,      -- planned|executing|completed|partial|failed|undone|expired
  created_utc        INTEGER NOT NULL,
  completed_utc      INTEGER,
  item_count         INTEGER NOT NULL,
  total_bytes        INTEGER NOT NULL DEFAULT 0,
  risk_class         TEXT NOT NULL DEFAULT 'unknown',
  confirmed_by_user  INTEGER NOT NULL DEFAULT 1,
  user_request       TEXT
);

CREATE INDEX ix_operations_status ON operations(status);
CREATE INDEX ix_operations_created ON operations(created_utc DESC);

CREATE TABLE operation_items (
  operation_id  TEXT NOT NULL REFERENCES operations(id) ON DELETE CASCADE,
  seq           INTEGER NOT NULL,
  source_path   TEXT NOT NULL,
  dest_path     TEXT,
  status        TEXT NOT NULL DEFAULT 'pending', -- pending|completed|failed|skipped|undone|unrecoverable
  recycle_id    TEXT,
  error_code    TEXT,
  PRIMARY KEY (operation_id, seq)
);
