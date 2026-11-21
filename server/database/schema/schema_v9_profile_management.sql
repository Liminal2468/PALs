-- Profile preferences, account-management audits, and safe page metadata.
-- Apply after schema_v8_mfa.sql with the migration/DBA account.
-- This migration does not grant privileges and does not expose filesystem paths.

-- Run this file against the selected target database, for example:
-- mariadb palsysdb < schema_v9_profile_management.sql

START TRANSACTION;

ALTER TABLE student_profiles
  ADD COLUMN IF NOT EXISTS bio VARCHAR(300) NULL AFTER strand,
  ADD COLUMN IF NOT EXISTS section VARCHAR(40) NULL AFTER bio;

CREATE TABLE IF NOT EXISTS user_subject_preferences (
  user_id BIGINT UNSIGNED NOT NULL,
  subject_id BIGINT UNSIGNED NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (user_id, subject_id),
  CONSTRAINT fk_user_subject_preferences_user
    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
  CONSTRAINT fk_user_subject_preferences_subject
    FOREIGN KEY (subject_id) REFERENCES subjects (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS account_audits (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  actor_user_id BIGINT UNSIGNED NOT NULL,
  target_user_id BIGINT UNSIGNED NOT NULL,
  audit_action VARCHAR(64) NOT NULL,
  details JSON NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_account_audits_target_time (target_user_id, created_at),
  KEY idx_account_audits_actor_time (actor_user_id, created_at),
  CONSTRAINT fk_account_audits_actor
    FOREIGN KEY (actor_user_id) REFERENCES users (id) ON DELETE RESTRICT,
  CONSTRAINT fk_account_audits_target
    FOREIGN KEY (target_user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS session_audits (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  actor_user_id BIGINT UNSIGNED NOT NULL,
  session_id BIGINT UNSIGNED NOT NULL,
  previous_status VARCHAR(32) NOT NULL,
  next_status VARCHAR(32) NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_session_audits_session_time (session_id, created_at),
  CONSTRAINT fk_session_audits_actor
    FOREIGN KEY (actor_user_id) REFERENCES users (id) ON DELETE RESTRICT,
  CONSTRAINT fk_session_audits_session
    FOREIGN KEY (session_id) REFERENCES study_sessions (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS platform_pages (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  page_key VARCHAR(64) NOT NULL,
  title VARCHAR(120) NOT NULL,
  route VARCHAR(160) NOT NULL,
  is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
  show_in_navigation BOOLEAN NOT NULL DEFAULT TRUE,
  allowed_roles JSON NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY ux_platform_pages_key (page_key),
  UNIQUE KEY ux_platform_pages_route (route)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS platform_page_audits (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  actor_user_id BIGINT UNSIGNED NOT NULL,
  page_id BIGINT UNSIGNED NOT NULL,
  audit_action VARCHAR(64) NOT NULL,
  details JSON NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_platform_page_audits_page_time (page_id, created_at),
  CONSTRAINT fk_platform_page_audits_actor
    FOREIGN KEY (actor_user_id) REFERENCES users (id) ON DELETE RESTRICT,
  CONSTRAINT fk_platform_page_audits_page
    FOREIGN KEY (page_id) REFERENCES platform_pages (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO platform_pages (page_key, title, route, is_enabled, show_in_navigation, allowed_roles)
VALUES
  ('home', 'Home', 'pages/home.html', TRUE, TRUE, JSON_ARRAY('student', 'tutor', 'tutee', 'admin', 'developer')),
  ('tutors', 'Tutors', 'pages/tutors.html', TRUE, TRUE, JSON_ARRAY('student', 'tutor', 'tutee', 'admin', 'developer')),
  ('tutees', 'Tutees', 'pages/tutees.html', TRUE, TRUE, JSON_ARRAY('student', 'tutor', 'tutee', 'admin', 'developer')),
  ('sessions', 'Sessions', 'pages/sessions.html', TRUE, TRUE, JSON_ARRAY('student', 'tutor', 'tutee', 'admin', 'developer')),
  ('resources', 'Resources', 'pages/resources.html', TRUE, TRUE, JSON_ARRAY('student', 'tutor', 'tutee', 'admin', 'developer')),
  ('analytics', 'Analytics', 'pages/analytics.html', TRUE, TRUE, JSON_ARRAY('student', 'tutor', 'tutee', 'admin', 'developer')),
  ('account', 'Account', 'pages/user.html', TRUE, TRUE, JSON_ARRAY('student', 'tutor', 'tutee', 'admin', 'developer'))
ON DUPLICATE KEY UPDATE
  title = VALUES(title),
  route = VALUES(route);

COMMIT;
