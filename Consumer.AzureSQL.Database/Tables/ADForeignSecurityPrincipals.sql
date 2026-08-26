-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Stores Active Directory foreign security principals
-- ========================================================
CREATE TABLE [dbo].[ADForeignSecurityPrincipals]
(
	[ADForeignSecurityPrincipalsId] VARCHAR(36) NOT NULL PRIMARY KEY DEFAULT LOWER(NEWSEQUENTIALID()),
	[ADForeignSecurityPrincipalsIdentity] INT IDENTITY(0, 1) NOT NULL,
	[ADForeignSecurityPrincipalsUpdated] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADForeignSecurityPrincipalsUpdatedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADForeignSecurityPrincipalsInserted] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADForeignSecurityPrincipalsInsertedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADForeignSecurityPrincipalsDeleted] DATETIME2 NULL,
	[ADForeignSecurityPrincipalsRowVersion] ROWVERSION NOT NULL,
	[ADForeignSecurityPrincipalsDomainFQDN] VARCHAR(256) NULL,
	[objectGUID] VARCHAR(36) NOT NULL,
	[objectSID] VARCHAR(100) NULL,
	[cn] VARCHAR(128) NOT NULL,
	[distinguishedName] VARCHAR(1700) NOT NULL, --2048 required but 1700 could be indexed
	[msDS-parentdistname] VARCHAR(2048) NULL,
	[displayName] VARCHAR(256) NULL,
	[description] VARCHAR(1024) NULL,
	[msDS-PrincipalName] VARCHAR(128) NULL,
	[objectClass] VARCHAR(500)
)

GO



CREATE TRIGGER [dbo].[Trigger_ADForeignSecurityPrincipals_ADForeignSecurityPrincipalsUpdated-UPDATE]	ON [dbo].[ADForeignSecurityPrincipals]	FOR UPDATE
As
	BEGIN
		UPDATE
			[ADForeignSecurityPrincipals]
		SET
			[ADForeignSecurityPrincipalsUpdated] = GETUTCDATE(),
			[ADForeignSecurityPrincipalsUpdatedBy] = SUSER_SNAME()
		WHERE
			[ADForeignSecurityPrincipalsId] In
			(
				SELECT DISTINCT
						[ADForeignSecurityPrincipalsId]
				FROM
						Inserted
			)
	END
GO