CREATE TABLE operator_ballots (
  season TEXT NOT NULL,
  operator TEXT NOT NULL,
  voter TEXT NOT NULL,
  kind TEXT NOT NULL CHECK (kind IN ('skill', 'equipment')),
  choices TEXT NOT NULL CHECK (json_valid(choices) AND json_type(choices) = 'array'),
  PRIMARY KEY (season, operator, voter, kind)
) WITHOUT ROWID;

CREATE TABLE operator_vote_totals (
  season TEXT NOT NULL,
  operator TEXT NOT NULL,
  kind TEXT NOT NULL,
  choice TEXT NOT NULL,
  votes INTEGER NOT NULL,
  PRIMARY KEY (season, operator, kind, choice)
) WITHOUT ROWID;

-- Only a newly inserted ballot contributes to the totals, in the same transaction.
CREATE TRIGGER count_operator_ballot AFTER INSERT ON operator_ballots
BEGIN
  INSERT INTO operator_vote_totals (season, operator, kind, choice, votes)
    SELECT NEW.season, NEW.operator, NEW.kind, CAST(value AS TEXT), 1
    FROM json_each(NEW.choices) WHERE true
    ON CONFLICT (season, operator, kind, choice) DO UPDATE SET votes = votes + 1;
END;
