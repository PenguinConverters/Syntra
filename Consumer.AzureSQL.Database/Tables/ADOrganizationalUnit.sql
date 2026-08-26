-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Stores Active Directory organizational units
-- ========================================================
CREATE TABLE [dbo].[ADOrganizationalUnit]
(
	[ADOrganizationalUnitId] VARCHAR(36) NOT NULL PRIMARY KEY DEFAULT LOWER(NEWSEQUENTIALID()),
	[ADOrganizationalUnitIdentity] INT IDENTITY(0, 1) NOT NULL,
	[ADOrganizationalUnitUpdated] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADOrganizationalUnitUpdatedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADOrganizationalUnitInserted] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADOrganizationalUnitInsertedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADOrganizationalUnitDeleted] DATETIME2 NULL,
	[ADOrganizationalUnitRowVersion] ROWVERSION NOT NULL,
	[ADOrganizationalUnitDomainFQDN] VARCHAR(256) NULL,
	[objectGUID] VARCHAR(36) NOT NULL,
	[cn] VARCHAR(128) NULL,
	[distinguishedName] VARCHAR(1700) NOT NULL, --2048 required but 1700 could be indexed
	[canonicalName] VARCHAR(1700) NOT NULL, --2048 required but 1700 could be indexed
	[msDS-parentdistname] VARCHAR(2048) NOT NULL,
	[displayName] VARCHAR(256) NULL,
	[description] VARCHAR(1024) NULL,
	[objectClass] VARCHAR(500)
)

GO


CREATE NONCLUSTERED INDEX IX_ADOrganizationalUnit_distinguishedName ON [dbo].[ADOrganizationalUnit]
(
	[distinguishedName] ASC
)
GO

CREATE UNIQUE INDEX IX_ADOrganizationalUnit_objectGUID ON [dbo].[ADOrganizationalUnit]
(
	[objectGUID] ASC
)
GO
