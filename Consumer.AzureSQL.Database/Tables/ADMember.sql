-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Stores Active Directory group membership edges
-- ========================================================
CREATE TABLE [dbo].[ADMember]
(
	[ADMemberId] VARCHAR(36) NOT NULL PRIMARY KEY DEFAULT LOWER(NEWSEQUENTIALID()),
	[ADMemberIdentity] INT IDENTITY(0, 1) NOT NULL,
	[ADMemberUpdated] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADMemberUpdatedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADMemberInserted] DATETIME2 NULL DEFAULT GETUTCDATE(),
	[ADMemberInsertedBy] VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	[ADMemberDeleted] DATETIME2 NULL,
	[ADMemberRowVersion] ROWVERSION NOT NULL,
	[ADMemberDomainFQDN] VARCHAR(256) NULL,
	[groupDistinguishedName] VARCHAR(1700) NOT NULL, --2048 required but 1700 could be indexed
	[memberDistinguishedName] VARCHAR(1700) NOT NULL --2048 required but 1700 could be indexed
)

GO

CREATE TRIGGER [dbo].[Trigger_ADMember_ADMemberUpdated-UPDATE]	ON [dbo].[ADMember]	FOR UPDATE
As
	BEGIN
		UPDATE
			[ADMember]
		SET
			[ADMemberUpdated] = GETUTCDATE(),
			[ADMemberUpdatedBy] = SUSER_SNAME()
		WHERE
			[ADMemberId] In
			(
				SELECT DISTINCT
						[ADMemberId]
				FROM
						Inserted
			)
	END
GO

CREATE NONCLUSTERED INDEX IX_ADMember_groupDistinguishedName ON [dbo].[ADMember]
(
	[groupDistinguishedName] ASC
)
GO

CREATE NONCLUSTERED INDEX IX_ADMember_memberDistinguishedName ON [dbo].[ADMember]
(
	[memberDistinguishedName] ASC
)
GO

CREATE NONCLUSTERED INDEX [IX_ADMember_Multiple] ON [dbo].[ADMember]
(
	[ADMemberDomainFQDN] ASC
)
INCLUDE([ADMemberId],[ADMemberIdentity],[ADMemberUpdated],[ADMemberUpdatedBy],[ADMemberInserted],[ADMemberInsertedBy],[ADMemberDeleted],[groupDistinguishedName],[memberDistinguishedName]) WITH (SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF) ON [PRIMARY]
GO

CREATE STATISTICS [ST_stat_18099105_1_7] ON [dbo].[ADMember]([ADMemberId], [ADMemberDeleted])
GO

CREATE NONCLUSTERED INDEX [IX_index_ADMember_5_18099105__K10_1_7_11] ON [dbo].[ADMember]
(
	[groupDistinguishedName] ASC
)
INCLUDE([ADMemberId],[ADMemberDeleted],[memberDistinguishedName]) WITH (SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF) ON [PRIMARY]
GO