USE devpals;

START TRANSACTION;

INSERT INTO roles (role_key, description)
VALUES
  ('developer', 'Development-side platform account')
ON DUPLICATE KEY UPDATE description = VALUES(description);

INSERT INTO users (
  external_subject,
  email,
  display_name,
  account_status,
  terms_version,
  terms_accepted_at
)
SELECT
  'dev:reime@pals.me',
  'reime@pals.me',
  'Reime',
  'active',
  'development',
  CURRENT_TIMESTAMP(3)
WHERE NOT EXISTS (
  SELECT 1 FROM users WHERE email = 'reime@pals.me'
);

INSERT INTO users (
  external_subject,
  email,
  display_name,
  account_status,
  terms_version,
  terms_accepted_at
)
SELECT
  'dev:indev.sys@pals.me',
  'indev.sys@pals.me',
  'InDev System',
  'active',
  'development',
  CURRENT_TIMESTAMP(3)
WHERE NOT EXISTS (
  SELECT 1 FROM users WHERE email = 'indev.sys@pals.me'
);

INSERT INTO user_roles (user_id, role_id)
SELECT u.id, r.id
FROM users u
JOIN roles r ON r.role_key = 'developer'
WHERE u.email IN ('reime@pals.me', 'indev.sys@pals.me')
  AND NOT EXISTS (
    SELECT 1
    FROM user_roles ur
    WHERE ur.user_id = u.id
      AND ur.role_id = r.id
  );

COMMIT;