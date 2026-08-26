-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Stores Active Directory user objects
-- ========================================================
CREATE TABLE [dbo].[ADUser]
(
	[ADUserId] VARCHAR(36) NOT NULL PRIMARY KEY DEFAULT LOWER(NEWSEQUENTIALID()),
	[ADUserIdentity] INT IDENTITY(0, 1) NOT NULL,
	[ADUserUpdated] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADUserUpdatedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADUserInserted] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADUserInsertedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADUserDeleted] DATETIME2 NULL,
	[ADUserRowVersion] ROWVERSION NOT NULL,
	[ADUserDomainFQDN] VARCHAR(256) NULL,
	[objectGUID] VARCHAR(36) NOT NULL,
	[objectSID] VARCHAR(100) NULL,
	[cn] VARCHAR(128) NOT NULL,
	[sAMAccountName] VARCHAR(128) NULL,
	[distinguishedName] VARCHAR(1700) NOT NULL, --2048 required but 1700 could be indexed
	[msDS-parentdistname] VARCHAR(2048) NULL,
	[adminCount] INT NULL,
	[displayName] VARCHAR(256) NULL,
	[description] VARCHAR(1024) NULL,
	[userPrincipalName] VARCHAR(512) NULL,
	[mail] VARCHAR(513) NULL,
	[msDS-PrincipalName] VARCHAR(128) NULL,
    [userAccountControl] INT NOT NULL DEFAULT 0,
	[objectClass] VARCHAR(500), 
    [accountExpires] BIGINT NULL, 
    [lastLogonTimestamp] BIGINT NULL, 
    [pwdLastSet] BIGINT NULL
)

GO

CREATE TRIGGER [dbo].[Trigger_ADUser_ADUserUpdated-UPDATE]	ON [dbo].[ADUser]	FOR UPDATE
As
	BEGIN
		UPDATE
			[ADUser]
		SET
			[ADUserUpdated] = GETUTCDATE(),
			[ADUserUpdatedBy] = SUSER_SNAME()
		WHERE
			[ADUserId] In
			(
				SELECT DISTINCT
						[ADUserId]
				FROM
						Inserted
			)
	END
GO

CREATE NONCLUSTERED INDEX IX_ADUser_distinguishedName ON [dbo].[ADUser]
(
	[distinguishedName] ASC
)
GO

CREATE NONCLUSTERED INDEX IX_ADUser_objectGUID ON [dbo].[ADUser]
(
	[objectGUID] ASC
)
GO

CREATE NONCLUSTERED INDEX [IX_index_ADUser_5_1589580701__K14] ON [dbo].[ADUser]
(
	[distinguishedName] ASC
)WITH (SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF) ON [PRIMARY]
