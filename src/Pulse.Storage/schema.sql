-- Pulse v1: exported from the implementation specification.
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
PRAGMA busy_timeout = 3000;

CREATE TABLE activities (
    id                    TEXT PRIMARY KEY NOT NULL,
    name                  TEXT NOT NULL CHECK(length(trim(name)) BETWEEN 1 AND 64),
    icon_id               TEXT NOT NULL,
    description           TEXT NOT NULL DEFAULT '',
    kind                  TEXT NOT NULL CHECK(kind IN ('count', 'duration')),
    unit                  TEXT NOT NULL CHECK(length(unit) BETWEEN 1 AND 24),
    step_atoms            INTEGER NOT NULL CHECK(step_atoms > 0),
    group_gap_ms          INTEGER CHECK(group_gap_ms IS NULL OR group_gap_ms > 0),
    heatmap_breaks_json    TEXT NOT NULL DEFAULT '[5000,10000,20000]'
                                  CHECK(json_valid(heatmap_breaks_json)),
    visual_json           TEXT NOT NULL CHECK(json_valid(visual_json)),
    visual_key            TEXT NOT NULL UNIQUE,
    created_at_ms         INTEGER NOT NULL,
    created_local_date    TEXT NOT NULL,
    updated_at_ms         INTEGER NOT NULL,
    archived_at_ms        INTEGER,
    CHECK(kind <> 'duration' OR unit = 'min')
);

CREATE TABLE events (
    event_no              INTEGER PRIMARY KEY,
    id                    TEXT NOT NULL UNIQUE,
    activity_id           TEXT NOT NULL REFERENCES activities(id),
    recorded_at_ms        INTEGER NOT NULL,
    local_date            TEXT NOT NULL CHECK(length(local_date) = 10),
    utc_offset_minutes    INTEGER NOT NULL CHECK(utc_offset_minutes BETWEEN -840 AND 840),
    delta_steps           INTEGER NOT NULL CHECK(delta_steps IN (-1, 1)),
    delta_amount_atoms    INTEGER NOT NULL,
    delta_units           INTEGER CHECK(delta_units IS NULL OR (delta_steps = 1 AND delta_units > 0) OR (delta_steps = -1 AND delta_units < 0)),
    reverses_id           TEXT REFERENCES events(id),
    CHECK(
        (delta_steps = 1 AND delta_amount_atoms > 0 AND reverses_id IS NULL)
        OR
        (delta_steps = -1 AND delta_amount_atoms < 0 AND reverses_id IS NOT NULL)
    )
);

CREATE INDEX idx_events_activity_day
    ON events(activity_id, local_date, event_no);
CREATE INDEX idx_events_day_activity
    ON events(local_date, activity_id);
CREATE UNIQUE INDEX idx_events_one_reversal
    ON events(reverses_id) WHERE reverses_id IS NOT NULL;

PRAGMA user_version = 3;
