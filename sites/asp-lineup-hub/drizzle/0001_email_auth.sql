CREATE TABLE accounts (
  id TEXT PRIMARY KEY,
  email TEXT NOT NULL UNIQUE,
  created_at INTEGER NOT NULL
);
--> statement-breakpoint
CREATE TABLE auth_sessions (
  token_hash TEXT PRIMARY KEY,
  account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  expires_at INTEGER NOT NULL
);
--> statement-breakpoint
CREATE INDEX auth_sessions_expiration ON auth_sessions(expires_at);
--> statement-breakpoint
CREATE TABLE auth_email_codes (
  id TEXT PRIMARY KEY,
  visitor TEXT NOT NULL,
  email TEXT NOT NULL,
  code_hash TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  expires_at INTEGER NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0
);
--> statement-breakpoint
CREATE INDEX auth_email_codes_expiration ON auth_email_codes(expires_at);
--> statement-breakpoint
CREATE TABLE auth_limits (
  key TEXT PRIMARY KEY,
  expires_at INTEGER NOT NULL,
  attempts INTEGER NOT NULL
);
--> statement-breakpoint
CREATE INDEX auth_limits_expiration ON auth_limits(expires_at);
--> statement-breakpoint
ALTER TABLE lineups ADD COLUMN account_id TEXT REFERENCES accounts(id) ON DELETE SET NULL;
