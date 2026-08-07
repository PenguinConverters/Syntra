-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1RoleType
-- Description: Role type enumeration
-- =============================================
CREATE TABLE [dbo].[S1RoleType]
(
    [S1RoleTypeId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    [S1RoleTypeIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(64) NOT NULL,
    [DisplayName] NVARCHAR(128) NULL,
    [S1RoleTypeInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1RoleTypeInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1RoleTypeUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1RoleTypeUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1RoleTypeDeleted] DATETIME2 NULL,
    [S1RoleTypeRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1RoleType_UPDATE]
ON [dbo].[S1RoleType]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1RoleTypeUpdated] = GETUTCDATE(),
        [S1RoleTypeUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1RoleType] t
    INNER JOIN inserted i ON t.[S1RoleTypeId] = i.[S1RoleTypeId];
END
GO
