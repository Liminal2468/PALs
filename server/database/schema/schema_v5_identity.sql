-- ASP.NET Core Identity persistence for local PALs credentials.
-- Apply after schema_v1.sql and schema_v4_developer_credentials.sql.
-- This preserves PALs users/roles and links an Identity user through pal_user_id.
-- Do not insert plaintext passwords. Use the server seed command instead.

USE devpals;

START TRANSACTION;

CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
  `MigrationId` VARCHAR(150) NOT NULL,
  `ProductVersion` VARCHAR(32) NOT NULL,
  PRIMARY KEY (`MigrationId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS identity_roles (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Name VARCHAR(256) NULL,
  NormalizedName VARCHAR(256) NULL,
  ConcurrencyStamp LONGTEXT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY RoleNameIndex (NormalizedName)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS identity_users (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  pal_user_id BIGINT NULL,
  UserName VARCHAR(256) NULL,
  NormalizedUserName VARCHAR(256) NULL,
  Email VARCHAR(256) NULL,
  NormalizedEmail VARCHAR(256) NULL,
  EmailConfirmed TINYINT(1) NOT NULL,
  PasswordHash LONGTEXT NULL,
  SecurityStamp LONGTEXT NULL,
  ConcurrencyStamp LONGTEXT NULL,
  PhoneNumber LONGTEXT NULL,
  PhoneNumberConfirmed TINYINT(1) NOT NULL,
  TwoFactorEnabled TINYINT(1) NOT NULL,
  LockoutEnd DATETIME NULL,
  LockoutEnabled TINYINT(1) NOT NULL,
  AccessFailedCount INT NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UserNameIndex (NormalizedUserName),
  KEY EmailIndex (NormalizedEmail),
  UNIQUE KEY ux_identity_users_pal_user_id (pal_user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS identity_user_claims (
  Id INT NOT NULL AUTO_INCREMENT,
  UserId BIGINT NOT NULL,
  ClaimType LONGTEXT NULL,
  ClaimValue LONGTEXT NULL,
  PRIMARY KEY (Id),
  KEY IX_identity_user_claims_UserId (UserId),
  CONSTRAINT FK_identity_user_claims_identity_users_UserId
    FOREIGN KEY (UserId) REFERENCES identity_users (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS identity_role_claims (
  Id INT NOT NULL AUTO_INCREMENT,
  RoleId BIGINT NOT NULL,
  ClaimType LONGTEXT NULL,
  ClaimValue LONGTEXT NULL,
  PRIMARY KEY (Id),
  KEY IX_identity_role_claims_RoleId (RoleId),
  CONSTRAINT FK_identity_role_claims_identity_roles_RoleId
    FOREIGN KEY (RoleId) REFERENCES identity_roles (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS identity_user_roles (
  UserId BIGINT NOT NULL,
  RoleId BIGINT NOT NULL,
  PRIMARY KEY (UserId, RoleId),
  KEY IX_identity_user_roles_RoleId (RoleId),
  CONSTRAINT FK_identity_user_roles_identity_users_UserId
    FOREIGN KEY (UserId) REFERENCES identity_users (Id) ON DELETE CASCADE,
  CONSTRAINT FK_identity_user_roles_identity_roles_RoleId
    FOREIGN KEY (RoleId) REFERENCES identity_roles (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS identity_user_logins (
  LoginProvider VARCHAR(255) NOT NULL,
  ProviderKey VARCHAR(255) NOT NULL,
  ProviderDisplayName LONGTEXT NULL,
  UserId BIGINT NOT NULL,
  PRIMARY KEY (LoginProvider, ProviderKey),
  KEY IX_identity_user_logins_UserId (UserId),
  CONSTRAINT FK_identity_user_logins_identity_users_UserId
    FOREIGN KEY (UserId) REFERENCES identity_users (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS identity_user_tokens (
  UserId BIGINT NOT NULL,
  LoginProvider VARCHAR(255) NOT NULL,
  Name VARCHAR(255) NOT NULL,
  Value LONGTEXT NULL,
  PRIMARY KEY (UserId, LoginProvider, Name),
  CONSTRAINT FK_identity_user_tokens_identity_users_UserId
    FOREIGN KEY (UserId) REFERENCES identity_users (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261117055009_IdentityPersistenceV1', '10.0.12')
ON DUPLICATE KEY UPDATE `ProductVersion` = VALUES(`ProductVersion`);

COMMIT;
