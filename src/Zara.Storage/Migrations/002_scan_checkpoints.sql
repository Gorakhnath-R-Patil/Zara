-- Migration 002: scan_checkpoints — resumable-scan support for T14/T15.
--
-- Checkpoint granularity is per TOP-LEVEL CHILD of a scan root, not per
-- directory throughout the whole tree: WalkScanner's BFS queue is internal
-- to a single Walk() call and isn't itself persistable mid-traversal without
-- invasive changes to already-shipped, already-tested code (T13). Instead,
-- ScanOrchestrator treats each top-level child of the root as an independent
-- unit of work, walks it completely, and only then marks it done here. A
-- kill mid-scan re-walks whichever single top-level child was in flight (its
-- upserts are idempotent, so that's wasted work, not wrong data) but skips
-- every child already marked complete — which is what actually matters for
-- not restarting a 500k-file scan from zero. See TRACKER.md's decision log
-- for the trade-off and what would replace this if it proves insufficient.

CREATE TABLE scan_checkpoints (
  id             INTEGER PRIMARY KEY,
  volume_id      INTEGER NOT NULL REFERENCES volumes(id) ON DELETE CASCADE,
  root_path      TEXT    NOT NULL,
  child_name     TEXT    NOT NULL,
  completed_utc  INTEGER NOT NULL,
  UNIQUE(volume_id, root_path, child_name)
);

CREATE INDEX ix_scan_checkpoints_lookup ON scan_checkpoints(volume_id, root_path);
