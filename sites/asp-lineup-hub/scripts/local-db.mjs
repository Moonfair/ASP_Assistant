import { DatabaseSync } from 'node:sqlite';
import { readFileSync, readdirSync } from 'node:fs';

const migrations = new URL('../drizzle/', import.meta.url);

export function openDatabase(path) {
  const database = new DatabaseSync(path);
  database.exec('PRAGMA busy_timeout = 5000; PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;');
  const transaction = (execute) => {
    database.exec('BEGIN IMMEDIATE');
    try {
      const result = execute();
      database.exec('COMMIT');
      return result;
    } catch (error) {
      database.exec('ROLLBACK');
      throw error;
    }
  };

  database.exec('CREATE TABLE IF NOT EXISTS schema_migrations (name TEXT PRIMARY KEY)');
  const applied = database.prepare('SELECT name FROM schema_migrations WHERE name = ?');
  const record = database.prepare('INSERT INTO schema_migrations VALUES (?)');
  for (const name of readdirSync(migrations).filter(name => /^\d{4}_.+\.sql$/.test(name)).sort()) {
    transaction(() => {
      if (applied.get(name)) return;
      database.exec(readFileSync(new URL(name, migrations), 'utf8'));
      record.run(name);
    });
  }

  const executions = new WeakMap();
  return {
    prepare(sql) {
      const statement = database.prepare(sql);
      function bind(...values) {
        const query = {
          first: async () => statement.get(...values) ?? null,
          all: async () => ({ results: statement.all(...values) }),
          run: async () => statement.run(...values)
        };
        executions.set(query, () => statement.run(...values));
        return query;
      }
      const query = { ...bind(), bind };
      executions.set(query, () => statement.run());
      return query;
    },
    async batch(statements) {
      return transaction(() => statements.map(query => executions.get(query)()));
    },
    close() {
      database.close();
    }
  };
}
