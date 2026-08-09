-- Migration 003: parent_id backfill support.
--
-- files.parent_id was left NULL by the M2 indexing pipeline (see
-- TRACKER.md's decision log): populating it at WRITE time would require
-- knowing a parent directory's assigned `id` before its children are
-- written, which ScanOrchestrator's streaming/batched writer can't
-- guarantee in topological order for large subtrees (a directory's own row
-- can land in a LATER batch than its grandchildren's).
--
-- Instead, every row now also stores parent_path_hash — computed the exact
-- same way as path_hash (§22), but over the PARENT's canonical path. That
-- needs no id lookup at write time at all. A single UPDATE with a
-- correlated subquery (Zara.Indexing.ParentIdBackfiller) then resolves
-- every parent_id in one pass, regardless of write order, once the parent
-- rows exist.

ALTER TABLE files ADD COLUMN parent_path_hash INTEGER;

-- Makes ParentIdBackfiller's correlated subquery (matching parent_path_hash
-- against path_hash within a volume) an index lookup instead of a scan.
CREATE INDEX ix_files_pathhash ON files(volume_id, path_hash);
