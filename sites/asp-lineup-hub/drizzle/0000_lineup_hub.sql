CREATE TABLE IF NOT EXISTS lineups (
  id TEXT PRIMARY KEY,
  created_at INTEGER NOT NULL,
  data TEXT NOT NULL
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS comments (
  id TEXT PRIMARY KEY,
  lineup_id TEXT NOT NULL,
  text TEXT NOT NULL,
  created_at INTEGER NOT NULL
);
--> statement-breakpoint
CREATE INDEX IF NOT EXISTS comments_by_lineup ON comments(lineup_id, created_at DESC);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS ratings (
  lineup_id TEXT NOT NULL,
  voter TEXT NOT NULL,
  score INTEGER NOT NULL CHECK (score BETWEEN 1 AND 5),
  PRIMARY KEY(lineup_id, voter)
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS challenges (
  id TEXT PRIMARY KEY,
  visitor TEXT NOT NULL,
  action TEXT NOT NULL,
  target TEXT NOT NULL,
  answer_hash TEXT NOT NULL,
  expires INTEGER NOT NULL
);
--> statement-breakpoint
CREATE INDEX IF NOT EXISTS challenges_by_expiration ON challenges(expires);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS challenge_limits (
  key TEXT PRIMARY KEY,
  expires INTEGER NOT NULL,
  attempts INTEGER NOT NULL
);
--> statement-breakpoint
CREATE INDEX IF NOT EXISTS challenge_limits_by_expiration ON challenge_limits(expires);
