-- Server-side browser sessions for the Mentos Flask host API.
-- Apply with a MariaDB administrator after schema_v1.sql.
USE devpals; -- replace this back to 'palsysdb' before being synced to the server

CREATE TABLE IF NOT EXISTS web_sessions (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  session_key VARCHAR(64) NOT NULL,
  session_data JSON NOT NULL,
  expires_at DATETIME(3) NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_web_sessions_session_key (session_key),
  KEY idx_web_sessions_expires_at (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
