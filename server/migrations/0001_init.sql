-- Tundu cloud database (Cloudflare D1 = SQLite). Times are unix seconds (UTC).
-- Never edit a migration that was applied: add a new numbered file instead (wrangler d1 migrations create tundu <name>).

-- One row per box. The id is public (shown in Settings for support calls); the secret is only on the box.
CREATE TABLE devices (
  id                TEXT PRIMARY KEY,            -- "d_" + 20 characters (100 random bits)
  secret_hash       TEXT NOT NULL,               -- hex SHA-256 of the device secret
  created_at        INTEGER NOT NULL,
  last_seen_at      INTEGER NOT NULL,
  model             TEXT,
  sdk               INTEGER,                     -- Android API level
  app_version       INTEGER,                     -- versionCode of the Tundu app
  pass_until        INTEGER NOT NULL DEFAULT 0,  -- Game Pass is active while now < pass_until
  blocked           INTEGER NOT NULL DEFAULT 0 CHECK (blocked IN (0, 1)),
  blocked_reason    TEXT,
  fail_count        INTEGER NOT NULL DEFAULT 0,  -- wrong codes in the current 24 h window
  fail_window_start INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX devices_created ON devices (created_at);
CREATE INDEX devices_seen ON devices (last_seen_at);

-- A print run of cards. Cards only work after their batch is activated (when the shop has paid / the box is
-- sold): stolen boxes of cards are worthless until then, and a lost batch can be voided.
CREATE TABLE batches (
  id           INTEGER PRIMARY KEY,
  label        TEXT NOT NULL,
  days         INTEGER NOT NULL CHECK (days BETWEEN 1 AND 3660),
  count        INTEGER NOT NULL,
  status       TEXT NOT NULL CHECK (status IN ('creating', 'created', 'active', 'void')),
  created_at   INTEGER NOT NULL,
  activated_at INTEGER,
  voided_at    INTEGER,
  note         TEXT
);

CREATE TABLE cards (
  id             INTEGER PRIMARY KEY,
  code_hash      TEXT NOT NULL UNIQUE,           -- hex HMAC-SHA256(CODE_PEPPER, code); the code itself is never stored
  last4          TEXT NOT NULL,                  -- for support ("the card ending in 7KQ2")
  batch_id       INTEGER NOT NULL REFERENCES batches (id),
  days           INTEGER NOT NULL CHECK (days BETWEEN 1 AND 3660),
  -- 'claimed' exists only inside the redemption transaction (see src/redeem.ts)
  status         TEXT NOT NULL CHECK (status IN ('created', 'active', 'claimed', 'redeemed', 'void')),
  redeemed_by    TEXT REFERENCES devices (id),
  redeemed_at    INTEGER,
  redeem_request TEXT,                           -- the box's request id: a retried request gets the same answer
  voided_at      INTEGER,
  void_reason    TEXT
);
CREATE INDEX cards_batch ON cards (batch_id, status);
CREATE INDEX cards_device ON cards (redeemed_by);

-- Every change to a device's Game Pass, with the value before and after: the audit trail, and the way to check
-- devices.pass_until (it must equal the newest row's pass_until_after).
CREATE TABLE pass_ledger (
  id                INTEGER PRIMARY KEY,
  device_id         TEXT NOT NULL REFERENCES devices (id),
  kind              TEXT NOT NULL CHECK (kind IN ('redeem', 'grant', 'revoke', 'transfer_in', 'transfer_out')),
  card_id           INTEGER REFERENCES cards (id),
  seconds           INTEGER NOT NULL,            -- requested change (negative for revoke / transfer_out)
  pass_until_before INTEGER NOT NULL,
  pass_until_after  INTEGER NOT NULL,
  at                INTEGER NOT NULL,
  op                TEXT NOT NULL,               -- links the rows written by one operation
  actor             TEXT NOT NULL,               -- 'device' or 'admin'
  note              TEXT
);
CREATE UNIQUE INDEX ledger_one_redeem_per_card ON pass_ledger (card_id) WHERE kind = 'redeem';
CREATE INDEX ledger_device ON pass_ledger (device_id, id);
CREATE INDEX ledger_op ON pass_ledger (op);

-- Health reports from boxes (no personal data): started fine, crashed, update installed or failed. Used to watch
-- a staged rollout. Deleted after 90 days by the daily cron.
CREATE TABLE events (
  id          INTEGER PRIMARY KEY,
  device_id   TEXT NOT NULL,
  at          INTEGER NOT NULL,                  -- when the box says it happened
  received_at INTEGER NOT NULL,
  app_version INTEGER NOT NULL,
  kind        TEXT NOT NULL,
  detail      TEXT
);
CREATE INDEX events_received ON events (received_at);
CREATE INDEX events_version ON events (app_version, kind);
CREATE INDEX events_device ON events (device_id, id);

-- Every admin action.
CREATE TABLE admin_log (
  id     INTEGER PRIMARY KEY,
  at     INTEGER NOT NULL,
  action TEXT NOT NULL,
  detail TEXT NOT NULL,
  ip     TEXT
);
