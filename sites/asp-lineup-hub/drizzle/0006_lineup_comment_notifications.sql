CREATE TABLE lineup_comment_notifications (
  id TEXT PRIMARY KEY NOT NULL REFERENCES comments(id) ON DELETE CASCADE,
  lineup_id TEXT NOT NULL REFERENCES lineups(id) ON DELETE CASCADE,
  recipient_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  created_at INTEGER NOT NULL,
  read_at INTEGER
);
--> statement-breakpoint
CREATE INDEX lineup_comment_notifications_by_recipient
  ON lineup_comment_notifications (recipient_id, created_at DESC, id);
--> statement-breakpoint
CREATE INDEX lineup_comment_notifications_by_lineup ON lineup_comment_notifications (lineup_id);
