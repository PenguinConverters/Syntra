-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1OrganizationalUnit
-- Description: Organizational unit hierarchy
-- =============================================
CREATE TABLE [dbo].[S1OrganizationalUnit]
(
    [S1OrganizationalUnitId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1OrganizationalUnitIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(128) NOT NULL,
    [DisplayName] NVARCHAR(256) NULL,
    [ParentId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1OrganizationalUnit]([S1OrganizationalUnitId]),
    [S1OrganizationalUnitInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1OrganizationalUnitInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1OrganizationalUnitUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1OrganizationalUnitUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1OrganizationalUnitDeleted] DATETIME2 NULL,
    [S1OrganizationalUnitRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1OrganizationalUnit_UPDATE]
ON [dbo].[S1OrganizationalUnit]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1OrganizationalUnitUpdated] = GETUTCDATE(),
        [S1OrganizationalUnitUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1OrganizationalUnit] t
    INNER JOIN inserted i ON t.[S1OrganizationalUnitId] = i.[S1OrganizationalUnitId];
END
GO
