-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Stores Active Directory group managed service accounts
-- ========================================================
CREATE TABLE [dbo].[ADGroupManagedServiceAccount]
(
	[ADGroupManagedServiceAccountId] VARCHAR(36) NOT NULL PRIMARY KEY DEFAULT LOWER(NEWSEQUENTIALID()),
	[ADGroupManagedServiceAccountIdentity] INT IDENTITY(0, 1) NOT NULL,
	[ADGroupManagedServiceAccountUpdated] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADGroupManagedServiceAccountUpdatedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADGroupManagedServiceAccountInserted] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADGroupManagedServiceAccountInsertedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADGroupManagedServiceAccountDeleted] DATETIME2 NULL,
	[ADGroupManagedServiceAccountRowVersion] ROWVERSION NOT NULL,
	[ADGroupManagedServiceAccountDomainFQDN] VARCHAR(256) NULL,
	[objectGUID] VARCHAR(36) NOT NULL,
	[objectSID] VARCHAR(100) NULL,
	[cn] VARCHAR(128) NOT NULL,
	[sAMAccountName] VARCHAR(128) NULL,
	[distinguishedName] VARCHAR(1700) NOT NULL, --2048 required but 1700 could be indexed
	[displayName] VARCHAR(256) NULL,
	[msDS-PrincipalName] VARCHAR(128) NULL,
	[objectClass] VARCHAR(500)
)

GO

CREATE TRIGGER [dbo].[Trigger_ADGroupManagedServiceAccount_ADGroupManagedServiceAccountUpdated-UPDATE]	ON [dbo].[ADGroupManagedServiceAccount]	FOR UPDATE
As
	BEGIN
		UPDATE
			[ADGroupManagedServiceAccount]
		SET
			[ADGroupManagedServiceAccountUpdated] = GETUTCDATE(),
			[ADGroupManagedServiceAccountUpdatedBy] = SUSER_SNAME()
		WHERE
			[ADGroupManagedServiceAccountId] In
			(
				SELECT DISTINCT
						[ADGroupManagedServiceAccountId]
				FROM
						Inserted
			)
	END
GO