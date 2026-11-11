CREATE TABLE share_snapshots (
    token TEXT PRIMARY KEY NOT NULL,
    expires_at INTEGER NOT NULL,
    payload BLOB NOT NULL
);
CREATE INDEX share_snapshots_expiry ON share_snapshots (expires_at);
