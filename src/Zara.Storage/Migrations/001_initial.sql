-- Migration 001: volumes, files, folder_stats.
-- Deliberately excludes content/FTS/vector/embedding tables (file_content,
-- fts_content, file_chunks, vec_chunks, vec_files, embedding_meta) and
-- later-milestone tables (index_jobs, ignored_paths, watched_zones,
-- preferences, search_history, dir_frecency, ai_sessions) — those arrive in
-- their own migrations as the milestones that need them land (T14/T15 add
-- index_jobs; Phase 2 adds content/vector). See ARCHITECTURE.md §22.

CREATE TABLE volumes (
  id            INTEGER PRIMARY KEY,
  guid          TEXT    NOT NULL UNIQUE,
  serial        INTEGER NOT NULL,
  label         TEXT,
  filesystem    TEXT    NOT NULL,
  drive_letter  TEXT,
  is_removable  INTEGER NOT NULL DEFAULT 0,
  is_online     INTEGER NOT NULL DEFAULT 1,
  total_bytes   INTEGER,
  free_bytes    INTEGER,
  index_enabled INTEGER NOT NULL DEFAULT 1,
  usn_journal_id INTEGER,
  last_usn      INTEGER NOT NULL DEFAULT 0,
  scan_state    TEXT NOT NULL DEFAULT 'none',
  last_full_scan_utc INTEGER
);

CREATE TABLE files (
  id             INTEGER PRIMARY KEY,
  volume_id      INTEGER NOT NULL REFERENCES volumes(id) ON DELETE CASCADE,
  frn            INTEGER NOT NULL,
  parent_id      INTEGER REFERENCES files(id) ON DELETE CASCADE,
  name           TEXT    NOT NULL,
  name_folded    TEXT    NOT NULL,
  ext            TEXT,
  path_hash      INTEGER NOT NULL,
  depth          INTEGER NOT NULL,
  is_dir         INTEGER NOT NULL,
  size_bytes     INTEGER NOT NULL DEFAULT 0,
  alloc_bytes    INTEGER,
  created_utc    INTEGER,
  modified_utc   INTEGER,
  accessed_utc   INTEGER,
  attributes     INTEGER NOT NULL DEFAULT 0,
  mime_type      TEXT,
  type_class     TEXT,
  content_hash   TEXT,
  quick_hash     INTEGER,
  index_tier     INTEGER NOT NULL DEFAULT 1,
  content_state  TEXT NOT NULL DEFAULT 'none',
  content_error  TEXT,
  injection_score INTEGER NOT NULL DEFAULT 0,
  open_count     INTEGER NOT NULL DEFAULT 0,
  last_opened_utc INTEGER,
  indexed_utc    INTEGER NOT NULL,
  deleted_utc    INTEGER
);

CREATE UNIQUE INDEX ux_files_frn   ON files(volume_id, frn);
CREATE INDEX ux_files_parent       ON files(parent_id, name_folded);
CREATE INDEX ix_files_ext_size     ON files(ext, size_bytes DESC) WHERE deleted_utc IS NULL;
CREATE INDEX ix_files_modified     ON files(modified_utc DESC)    WHERE deleted_utc IS NULL;
CREATE INDEX ix_files_accessed     ON files(accessed_utc)         WHERE deleted_utc IS NULL;
CREATE INDEX ix_files_quickhash    ON files(quick_hash, size_bytes) WHERE is_dir = 0;
CREATE INDEX ix_files_content_hash ON files(content_hash)         WHERE content_hash IS NOT NULL;
CREATE INDEX ix_files_pending      ON files(content_state, index_tier)
                                          WHERE content_state IN ('queued','error');

CREATE TABLE folder_stats (
  file_id        INTEGER PRIMARY KEY REFERENCES files(id) ON DELETE CASCADE,
  direct_files   INTEGER NOT NULL DEFAULT 0,
  total_files    INTEGER NOT NULL DEFAULT 0,
  total_bytes    INTEGER NOT NULL DEFAULT 0,
  max_modified_utc INTEGER,
  dirty          INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX ix_folder_stats_size  ON folder_stats(total_bytes DESC);
CREATE INDEX ix_folder_stats_dirty ON folder_stats(dirty) WHERE dirty = 1;
