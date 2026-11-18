-- Short-lived TOTP replay protection for ASP.NET Identity authenticator codes.
-- Apply after schema_v5_identity.sql using the migration/DBA account.
-- Raw authenticator codes are never stored; code_hash is a SHA-256 digest.

USE devpals;

CREATE TABLE IF NOT EXISTS identity_mfa_replays (
  identity_user_id BIGINT NOT NULL,
  code_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  used_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (identity_user_id, code_hash),
  KEY idx_identity_mfa_replays_used_at (used_at),
  CONSTRAINT fk_identity_mfa_replays_user
    FOREIGN KEY (identity_user_id) REFERENCES identity_users (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
