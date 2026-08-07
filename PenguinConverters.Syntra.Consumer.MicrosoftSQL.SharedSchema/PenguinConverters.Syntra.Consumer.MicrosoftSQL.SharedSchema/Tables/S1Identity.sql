-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Identity
-- Description: User/identity table per SCIM RFC-7643
-- =============================================
CREATE TABLE [dbo].[S1Identity]
(
    [S1IdentityId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    [S1IdentityIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(128) NULL,
    [UserName] VARCHAR(256) NOT NULL,
    [DisplayName] NVARCHAR(256) NULL,
    [Email] VARCHAR(256) NULL,
    [GivenName] NVARCHAR(128) NULL,
    [FamilyName] NVARCHAR(128) NULL,
    [MiddleName] NVARCHAR(128) NULL,
    [Title] NVARCHAR(128) NULL,
    [Department] NVARCHAR(128) NULL,
    [Division] NVARCHAR(128) NULL,
    [CostCenter] VARCHAR(64) NULL,
    [ManagerId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1Identity]([S1IdentityId]),
    [S1OrganizationalUnitId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1OrganizationalUnit]([S1OrganizationalUnitId]),
    [Active] BIT NOT NULL DEFAULT 1,
    [S1IdentityInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1IdentityInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1IdentityUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1IdentityUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1IdentityDeleted] DATETIME2 NULL,
    [S1IdentityRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1Identity_UPDATE]
ON [dbo].[S1Identity]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1IdentityUpdated] = GETUTCDATE(),
        [S1IdentityUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Identity] t
    INNER JOIN inserted i ON t.[S1IdentityId] = i.[S1IdentityId];
END
GO
