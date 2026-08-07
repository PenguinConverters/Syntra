-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1TargetType
-- Description: Target type enumeration
-- =============================================
CREATE TABLE [dbo].[S1TargetType]
(
    [S1TargetTypeId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    [S1TargetTypeIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(64) NOT NULL,
    [DisplayName] NVARCHAR(128) NULL,
    [S1TargetTypeInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TargetTypeInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TargetTypeUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TargetTypeUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TargetTypeDeleted] DATETIME2 NULL,
    [S1TargetTypeRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1TargetType_UPDATE]
ON [dbo].[S1TargetType]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1TargetTypeUpdated] = GETUTCDATE(),
        [S1TargetTypeUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1TargetType] t
    INNER JOIN inserted i ON t.[S1TargetTypeId] = i.[S1TargetTypeId];
END
GO
