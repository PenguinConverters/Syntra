-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Stores Active Directory group objects
-- ========================================================
CREATE TABLE [dbo].[ADGroup]
(
	[ADGroupId] VARCHAR(36) NOT NULL PRIMARY KEY DEFAULT LOWER(NEWSEQUENTIALID()),
	[ADGroupIdentity] INT IDENTITY(0, 1) NOT NULL,
	[ADGroupUpdated] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADGroupUpdatedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADGroupInserted] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADGroupInsertedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADGroupDeleted] DATETIME2 NULL,
	[ADGroupRowVersion] ROWVERSION NOT NULL,
	[ADGroupDomainFQDN] VARCHAR(256) NULL,
	[objectGUID] VARCHAR(36) NOT NULL,
	[objectSID] VARCHAR(100) NULL,
	[cn] VARCHAR(128) NOT NULL,
	[sAMAccountName] VARCHAR(128) NULL,
	[distinguishedName] VARCHAR(1700) NOT NULL, --2048 required but 1700 could be indexed
	[msDS-parentdistname] VARCHAR(2048) NULL,
	[displayName] VARCHAR(256) NULL,
	[description] VARCHAR(1024) NULL,
	[msDS-PrincipalName] VARCHAR(128) NULL,
	[objectClass] VARCHAR(500), 
    [groupType] BIGINT DEFAULT(0) NOT NULL
)

GO

CREATE TRIGGER [dbo].[Trigger_ADGroup_ADGroupUpdated-UPDATE]	ON [dbo].[ADGroup]	FOR UPDATE
As
	BEGIN
		UPDATE
			[ADGroup]
		SET
			[ADGroupUpdated] = GETUTCDATE(),
			[ADGroupUpdatedBy] = SUSER_SNAME()
		WHERE
			[ADGroupId] In
			(
				SELECT DISTINCT
						[ADGroupId]
				FROM
						Inserted
			)
	END
GO

CREATE NONCLUSTERED INDEX IX_ADGroup_objectGUID ON [dbo].[ADGroup]
(
	[objectGUID] ASC
)
GO

CREATE NONCLUSTERED INDEX IX_ADGroup_distinguishedName ON [dbo].[ADGroup]
(
	[distinguishedName] ASC
)
GO

CREATE NONCLUSTERED INDEX [IX_index_ADGroup_5_1717581157__K13] ON [dbo].[ADGroup]
(
	[distinguishedName] ASC
)WITH (SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF) ON [PRIMARY]
