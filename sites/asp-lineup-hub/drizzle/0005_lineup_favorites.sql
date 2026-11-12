CREATE TABLE lineup_favorites (
  account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  lineup_id TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  PRIMARY KEY (account_id, lineup_id)
);
--> statement-breakpoint
CREATE INDEX lineup_favorites_by_lineup ON lineup_favorites (lineup_id);
--> statement-breakpoint
CREATE TRIGGER lineups_delete_interactions AFTER DELETE ON lineups
BEGIN
  DELETE FROM lineup_favorites WHERE lineup_id = OLD.id;
  DELETE FROM comments WHERE lineup_id = OLD.id;
  DELETE FROM ratings WHERE lineup_id = OLD.id;
END;
