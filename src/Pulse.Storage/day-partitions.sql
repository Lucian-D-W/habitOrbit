CREATE TABLE IF NOT EXISTS day_partitions (
    activity_id TEXT NOT NULL REFERENCES activities(id),
    local_date TEXT NOT NULL,
    body TEXT NOT NULL CHECK(json_valid(body)),
    PRIMARY KEY(activity_id,local_date)
);
CREATE TABLE IF NOT EXISTS group_receipts (
    id TEXT PRIMARY KEY NOT NULL,
    activity_id TEXT NOT NULL REFERENCES activities(id),
    input_json TEXT NOT NULL CHECK(json_valid(input_json)),
    result_json TEXT NOT NULL CHECK(json_valid(result_json))
);
