-- PALs schema v1 for MariaDB.
-- Apply as a database administrator after creating palsysdb.

USE palsysdb;

CREATE TABLE IF NOT EXISTS roles (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  role_key VARCHAR(32) NOT NULL,
  description VARCHAR(255) NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_roles_role_key (role_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO roles (role_key, description)
VALUES
  ('tutor', 'Student available to tutor peers'),
  ('tutee', 'Student seeking peer learning support'),
  ('admin', 'Platform administrators')
ON DUPLICATE KEY UPDATE description = VALUES(description);

-- Authentication is expected to be delegated to the STI eLMS. Do not store
-- eLMS passwords or local password hashes in this table.
CREATE TABLE IF NOT EXISTS users (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  external_subject VARCHAR(191) NOT NULL,
  email VARCHAR(254) NOT NULL,
  display_name VARCHAR(120) NOT NULL,
  account_status ENUM('pending', 'active', 'suspended', 'disabled') NOT NULL DEFAULT 'pending',
  terms_version VARCHAR(64) NULL,
  terms_accepted_at DATETIME(3) NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_users_external_subject (external_subject),
  UNIQUE KEY uq_users_email (email),
  KEY idx_users_account_status (account_status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS user_roles (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id BIGINT UNSIGNED NOT NULL,
  role_id BIGINT UNSIGNED NOT NULL,
  assigned_by_user_id BIGINT UNSIGNED NULL,
  assigned_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_user_roles_user_role (user_id, role_id),
  KEY idx_user_roles_role_id (role_id),
  CONSTRAINT fk_user_roles_user
    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
  CONSTRAINT fk_user_roles_role
    FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE RESTRICT,
  CONSTRAINT fk_user_roles_assigned_by
    FOREIGN KEY (assigned_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- This is managed by another code base
CREATE TABLE IF NOT EXISTS student_profiles (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id BIGINT UNSIGNED NOT NULL,
  grade_level TINYINT UNSIGNED NOT NULL,
  strand VARCHAR(100) NOT NULL,
  timezone VARCHAR(64) NOT NULL DEFAULT 'Asia/Manila',
  learning_preferences JSON NULL,
  profile_completed_at DATETIME(3) NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_student_profiles_user_id (user_id),
  KEY idx_student_profiles_grade_strand (grade_level, strand),
  CONSTRAINT chk_student_profiles_grade_level CHECK (grade_level IN (11, 12)),
  CONSTRAINT fk_student_profiles_user
    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS subjects (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  subject_code VARCHAR(64) NULL,
  name VARCHAR(160) NOT NULL,
  description VARCHAR(500) NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_subjects_subject_code (subject_code),
  UNIQUE KEY uq_subjects_name (name),
  KEY idx_subjects_is_active (is_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS topics (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  subject_id BIGINT UNSIGNED NOT NULL,
  name VARCHAR(160) NOT NULL,
  description VARCHAR(500) NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  PRIMARY KEY (id),
  UNIQUE KEY uq_topics_subject_name (subject_id, name),
  KEY idx_topics_subject_active (subject_id, is_active),
  CONSTRAINT fk_topics_subject
    FOREIGN KEY (subject_id) REFERENCES subjects (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS tutor_subjects (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tutor_user_id BIGINT UNSIGNED NOT NULL,
  subject_id BIGINT UNSIGNED NOT NULL,
  proficiency_level TINYINT UNSIGNED NOT NULL,
  verification_status ENUM('unverified', 'pending', 'verified', 'rejected') NOT NULL DEFAULT 'unverified',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_tutor_subject (tutor_user_id, subject_id),
  KEY idx_tutor_subjects_subject_level (subject_id, proficiency_level),
  -- The 1-5 scale is a prototype/API default, not a validated research scale.
  CONSTRAINT chk_tutor_subjects_proficiency CHECK (proficiency_level BETWEEN 1 AND 5),
  CONSTRAINT fk_tutor_subjects_user
    FOREIGN KEY (tutor_user_id) REFERENCES users (id) ON DELETE CASCADE,
  CONSTRAINT fk_tutor_subjects_subject
    FOREIGN KEY (subject_id) REFERENCES subjects (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS learning_requests (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  tutee_user_id BIGINT UNSIGNED NOT NULL,
  subject_id BIGINT UNSIGNED NOT NULL,
  topic_id BIGINT UNSIGNED NULL,
  current_proficiency TINYINT UNSIGNED NULL,
  requested_help TEXT NULL,
  preferred_format ENUM('one_to_one', 'group', 'either') NOT NULL DEFAULT 'either',
  request_status ENUM('open', 'matched', 'closed', 'cancelled') NOT NULL DEFAULT 'open',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_learning_requests_tutee_status (tutee_user_id, request_status),
  KEY idx_learning_requests_subject_status (subject_id, request_status),
  KEY idx_learning_requests_topic (topic_id),
  -- Keep the current prototype scale consistent with the matching API.
  CONSTRAINT chk_learning_requests_proficiency
    CHECK (current_proficiency IS NULL OR current_proficiency BETWEEN 1 AND 5),
  CONSTRAINT fk_learning_requests_tutee
    FOREIGN KEY (tutee_user_id) REFERENCES users (id) ON DELETE CASCADE,
  CONSTRAINT fk_learning_requests_subject
    FOREIGN KEY (subject_id) REFERENCES subjects (id) ON DELETE RESTRICT,
  CONSTRAINT fk_learning_requests_topic
    FOREIGN KEY (topic_id) REFERENCES topics (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Weekly recurring availability in the user's declared timezone.
-- day_of_week uses ISO-8601 numbering: Monday=1 through Sunday=7.
CREATE TABLE IF NOT EXISTS availability_slots (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id BIGINT UNSIGNED NOT NULL,
  day_of_week TINYINT UNSIGNED NOT NULL,
  start_time TIME NOT NULL,
  end_time TIME NOT NULL,
  timezone VARCHAR(64) NOT NULL DEFAULT 'Asia/Manila',
  effective_from DATE NULL,
  effective_until DATE NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_availability_user_day (user_id, day_of_week, is_active),
  CONSTRAINT chk_availability_day CHECK (day_of_week BETWEEN 1 AND 7),
  CONSTRAINT chk_availability_time CHECK (start_time < end_time),
  CONSTRAINT chk_availability_effective_dates
    CHECK (effective_from IS NULL OR effective_until IS NULL OR effective_from <= effective_until),
  CONSTRAINT fk_availability_slots_user
    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The final matching method and weights remain unvalidated; retain a version
-- and score components so the prototype can be audited and revised.
CREATE TABLE IF NOT EXISTS match_recommendations (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  learning_request_id BIGINT UNSIGNED NOT NULL,
  tutor_user_id BIGINT UNSIGNED NOT NULL,
  algorithm_version VARCHAR(64) NOT NULL,
  match_score DECIMAL(7, 4) NULL,
  score_components JSON NULL,
  explanation TEXT NULL,
  recommendation_status ENUM('recommended', 'accepted', 'declined', 'expired', 'withdrawn') NOT NULL DEFAULT 'recommended',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_match_request_tutor (learning_request_id, tutor_user_id),
  KEY idx_match_recommendations_status_score (recommendation_status, match_score),
  KEY idx_match_recommendations_tutor (tutor_user_id, recommendation_status),
  CONSTRAINT fk_match_recommendations_request
    FOREIGN KEY (learning_request_id) REFERENCES learning_requests (id) ON DELETE CASCADE,
  CONSTRAINT fk_match_recommendations_tutor
    FOREIGN KEY (tutor_user_id) REFERENCES users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS study_sessions (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  session_type ENUM('one_to_one', 'group') NOT NULL,
  subject_id BIGINT UNSIGNED NOT NULL,
  topic_id BIGINT UNSIGNED NULL,
  created_by_user_id BIGINT UNSIGNED NOT NULL,
  scheduled_start_utc DATETIME(3) NOT NULL,
  scheduled_end_utc DATETIME(3) NOT NULL,
  session_status ENUM('requested', 'confirmed', 'cancelled', 'completed', 'no_show') NOT NULL DEFAULT 'requested',
  cancellation_reason VARCHAR(500) NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_study_sessions_creator_start (created_by_user_id, scheduled_start_utc),
  KEY idx_study_sessions_status_start (session_status, scheduled_start_utc),
  CONSTRAINT chk_study_sessions_time CHECK (scheduled_start_utc < scheduled_end_utc),
  CONSTRAINT fk_study_sessions_creator
    FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE RESTRICT,
  CONSTRAINT fk_study_sessions_subject
    FOREIGN KEY (subject_id) REFERENCES subjects (id) ON DELETE RESTRICT,
  CONSTRAINT fk_study_sessions_topic
    FOREIGN KEY (topic_id) REFERENCES topics (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS session_participants (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  study_session_id BIGINT UNSIGNED NOT NULL,
  user_id BIGINT UNSIGNED NOT NULL,
  participant_role ENUM('tutor', 'tutee') NOT NULL,
  participation_status ENUM('invited', 'accepted', 'declined', 'removed') NOT NULL DEFAULT 'invited',
  joined_at DATETIME(3) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_session_participant (study_session_id, user_id),
  KEY idx_session_participants_user_status (user_id, participation_status),
  CONSTRAINT fk_session_participants_session
    FOREIGN KEY (study_session_id) REFERENCES study_sessions (id) ON DELETE CASCADE,
  CONSTRAINT fk_session_participants_user
    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS session_match_recommendations (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  study_session_id BIGINT UNSIGNED NOT NULL,
  match_recommendation_id BIGINT UNSIGNED NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_session_match_recommendation (study_session_id, match_recommendation_id),
  CONSTRAINT fk_session_matches_session
    FOREIGN KEY (study_session_id) REFERENCES study_sessions (id) ON DELETE CASCADE,
  CONSTRAINT fk_session_matches_recommendation
    FOREIGN KEY (match_recommendation_id) REFERENCES match_recommendations (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS session_feedback (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  study_session_id BIGINT UNSIGNED NOT NULL,
  reviewer_user_id BIGINT UNSIGNED NOT NULL,
  rating TINYINT UNSIGNED NOT NULL,
  learning_helpfulness TINYINT UNSIGNED NULL,
  comment TEXT NULL,
  moderation_status ENUM('pending', 'visible', 'flagged', 'removed') NOT NULL DEFAULT 'pending',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_session_feedback_reviewer (study_session_id, reviewer_user_id),
  KEY idx_session_feedback_session_status (study_session_id, moderation_status),
  CONSTRAINT chk_session_feedback_rating CHECK (rating BETWEEN 1 AND 5),
  CONSTRAINT chk_session_feedback_helpfulness
    CHECK (learning_helpfulness IS NULL OR learning_helpfulness BETWEEN 1 AND 5),
  CONSTRAINT fk_session_feedback_session
    FOREIGN KEY (study_session_id) REFERENCES study_sessions (id) ON DELETE CASCADE,
  CONSTRAINT fk_session_feedback_reviewer
    FOREIGN KEY (reviewer_user_id) REFERENCES users (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS research_consents (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id BIGINT UNSIGNED NOT NULL,
  consent_type ENUM('phase2_usage_analytics') NOT NULL,
  consent_version VARCHAR(64) NOT NULL,
  granted_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  withdrawn_at DATETIME(3) NULL,
  PRIMARY KEY (id),
  KEY idx_research_consents_user_type (user_id, consent_type, granted_at),
  CONSTRAINT fk_research_consents_user
    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Pseudonymous analytics are separate from account identifiers. The mapping
-- table is sensitive and should only be available to authorized server code.
CREATE TABLE IF NOT EXISTS research_participants (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  user_id BIGINT UNSIGNED NOT NULL,
  participant_key CHAR(36) NOT NULL,
  enrolled_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  withdrawn_at DATETIME(3) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_research_participants_user (user_id),
  UNIQUE KEY uq_research_participants_key (participant_key),
  CONSTRAINT fk_research_participants_user
    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS analytics_events (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  participant_key CHAR(36) NOT NULL,
  event_type VARCHAR(64) NOT NULL,
  feature_key VARCHAR(64) NOT NULL,
  duration_seconds INT UNSIGNED NULL,
  occurred_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_analytics_events_participant_time (participant_key, occurred_at),
  KEY idx_analytics_events_type_time (event_type, occurred_at),
  CONSTRAINT fk_analytics_events_participant
    FOREIGN KEY (participant_key) REFERENCES research_participants (participant_key) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS conversations (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  conversation_type ENUM('one_to_one', 'study_group') NOT NULL,
  title VARCHAR(160) NULL,
  created_by_user_id BIGINT UNSIGNED NOT NULL,
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  archived_at DATETIME(3) NULL,
  PRIMARY KEY (id),
  KEY idx_conversations_created_by (created_by_user_id, created_at),
  CONSTRAINT fk_conversations_creator
    FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS conversation_members (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  conversation_id BIGINT UNSIGNED NOT NULL,
  user_id BIGINT UNSIGNED NOT NULL,
  joined_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  left_at DATETIME(3) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_conversation_member (conversation_id, user_id),
  KEY idx_conversation_members_user (user_id, conversation_id),
  CONSTRAINT fk_conversation_members_conversation
    FOREIGN KEY (conversation_id) REFERENCES conversations (id) ON DELETE CASCADE,
  CONSTRAINT fk_conversation_members_user
    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS messages (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  conversation_id BIGINT UNSIGNED NOT NULL,
  sender_user_id BIGINT UNSIGNED NOT NULL,
  message_body TEXT NOT NULL,
  moderation_status ENUM('pending', 'visible', 'flagged', 'removed') NOT NULL DEFAULT 'pending',
  sent_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  edited_at DATETIME(3) NULL,
  PRIMARY KEY (id),
  KEY idx_messages_conversation_time (conversation_id, sent_at),
  KEY idx_messages_sender_time (sender_user_id, sent_at),
  CONSTRAINT fk_messages_conversation
    FOREIGN KEY (conversation_id) REFERENCES conversations (id) ON DELETE CASCADE,
  CONSTRAINT fk_messages_sender
    FOREIGN KEY (sender_user_id) REFERENCES users (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
