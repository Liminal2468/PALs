-- Provider identities, local admin credentials, and minimal authentication audit records.
-- Apply with a MariaDB administrator after schema_v1.sql and schema_v2_sessions.sql.
USE palsysdb;

ALTER TABLE users
  MODIFY external_subject VARCHAR(191) NULL,
  DROP INDEX uq_users_external_subject,
  DROP INDEX uq_users_email;

INSERT INTO roles (role_key, description)
VALUES
  ('student', 'Student using the PALs learning platform'),
  ('teacher', 'Teacher using the PALs learning platform')
ON DUPLICATE KEY UPDATE description = VALUES(description);

CREATE TABLE IF NOT EXISTS auth_identities (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id BIGINT UNSIGNED NOT NULL,
  provider VARCHAR(32) NOT NULL,
  provider_subject VARCHAR(191) NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_auth_identities_provider_subject (provider, provider_subject),
  KEY idx_auth_identities_user (user_id),
  CONSTRAINT fk_auth_identities_user
    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS admin_credentials (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id BIGINT UNSIGNED NOT NULL,
  username VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  password_hash VARCHAR(255) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_admin_credentials_username (username),
  UNIQUE KEY uq_admin_credentials_user (user_id),
  CONSTRAINT fk_admin_credentials_user
    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS auth_attempts (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  event VARCHAR(32) NOT NULL,
  provider VARCHAR(32) NOT NULL,
  user_id BIGINT UNSIGNED NULL,
  outcome ENUM('started', 'success', 'failure', 'pending') NOT NULL,
  reason_code VARCHAR(64) NOT NULL,
  client_ip_prefix VARCHAR(50) NULL,
  attempted_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_auth_attempts_time (attempted_at),
  KEY idx_auth_attempts_provider_time (provider, attempted_at),
  KEY idx_auth_attempts_user_time (user_id, attempted_at),
  CONSTRAINT fk_auth_attempts_user
    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
