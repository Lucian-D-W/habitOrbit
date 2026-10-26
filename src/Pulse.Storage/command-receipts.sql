-- Technical metadata, not a third business entity. Atomic durable UUID results,
-- including no_change, prevent a replay from undoing a later addition.
CREATE TABLE IF NOT EXISTS command_receipts (
    id TEXT PRIMARY KEY NOT NULL,
    activity_id TEXT NOT NULL REFERENCES activities(id),
    operation TEXT NOT NULL CHECK(operation IN ('add', 'decrement', 'set_today')),
    result_json TEXT NOT NULL CHECK(json_valid(result_json))
);
