-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Stores Active Directory computer objects
-- ========================================================
CREATE TABLE [dbo].[ADComputer]
(
	[ADComputerId] VARCHAR(36) NOT NULL PRIMARY KEY DEFAULT LOWER(NEWSEQUENTIALID()),
	[ADComputerIdentity] INT IDENTITY(0, 1) NOT NULL,
	[ADComputerUpdated] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADComputerUpdatedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADComputerInserted] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADComputerInsertedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADComputerDeleted] DATETIME2 NULL,
	[ADComputerRowVersion] ROWVERSION NOT NULL,
	[ADComputerDomainFQDN] VARCHAR(256) NULL,
	[objectGUID] VARCHAR(36) NOT NULL,
	[objectSID] VARCHAR(100) NULL,
	[cn] VARCHAR(128) NOT NULL,
	[sAMAccountName] VARCHAR(128) NULL,
	[distinguishedName] VARCHAR(1700) NOT NULL, --2048 required but 1700 could be indexed
	[displayName] VARCHAR(256) NULL,
	[msDS-PrincipalName] VARCHAR(128) NULL,
	[objectClass] VARCHAR(500),
	[description] VARCHAR(1024) NULL,
	[operatingSystem] VARCHAR(64) NULL,
	[operatingSystemVersion] VARCHAR(64) NULL,
	[managedBy] VARCHAR(1700) NULL --2048 required but 1700 could be indexed

)

GO

CREATE TRIGGER [dbo].[Trigger_ADComputer_ADComputerUpdated-UPDATE]	ON [dbo].[ADComputer]	FOR UPDATE
As
	BEGIN
		UPDATE
			[ADComputer]
		SET
			[ADComputerUpdated] = GETUTCDATE(),
			[ADComputerUpdatedBy] = SUSER_SNAME()
		WHERE
			[ADComputerId] In
			(
				SELECT DISTINCT
						[ADComputerId]
				FROM
						Inserted
			)
	END
GO

CREATE NONCLUSTERED INDEX [IX_index_ADComputer_5_754101727__K14] ON [dbo].[ADComputer]
(
	[distinguishedName] ASC
)WITH (SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF) ON [PRIMARY]
