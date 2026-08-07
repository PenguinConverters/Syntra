-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1EdgeType
-- Description: Edge type definitions with factor weight
-- =============================================
CREATE TABLE [dbo].[S1EdgeType]
(
    [S1EdgeTypeId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    [S1EdgeTypeIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(64) NOT NULL,
    [DisplayName] NVARCHAR(128) NULL,
    [Factor] TINYINT NOT NULL DEFAULT 75,
    [S1EdgeTypeInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1EdgeTypeInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1EdgeTypeUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1EdgeTypeUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1EdgeTypeDeleted] DATETIME2 NULL,
    [S1EdgeTypeRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1EdgeType_UPDATE]
ON [dbo].[S1EdgeType]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1EdgeTypeUpdated] = GETUTCDATE(),
        [S1EdgeTypeUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1EdgeType] t
    INNER JOIN inserted i ON t.[S1EdgeTypeId] = i.[S1EdgeTypeId];
END
GO
