-- Persistent HOST sessions and calendar schedule events.
-- Apply after schema_v1.sql through schema_v6_frontend_features.sql.

USE palsysdb;

CREATE TABLE IF NOT EXISTS web_sessions (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  session_key VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  session_data JSON NOT NULL,
  expires_at DATETIME(3) NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_web_sessions_key (session_key),
  KEY idx_web_sessions_expiry (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS schedule_events (
  event_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  created_by_user_id BIGINT UNSIGNED NOT NULL,
  on_date DATETIME(3) NOT NULL,
  after_date DATETIME(3) NOT NULL,
  event_name VARCHAR(180) NOT NULL,
  description TEXT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (event_id),
  KEY idx_schedule_events_active_dates (is_active, on_date, after_date),
  KEY idx_schedule_events_owner (created_by_user_id),
  CONSTRAINT chk_schedule_events_dates CHECK (on_date <= after_date),
  CONSTRAINT fk_schedule_events_owner FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
