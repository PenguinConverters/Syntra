-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1RelationshipType
-- Description: Relationship type definitions
-- =============================================
CREATE TABLE [dbo].[S1RelationshipType]
(
    [S1RelationshipTypeId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1RelationshipTypeIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(64) NOT NULL,
    [DisplayName] NVARCHAR(128) NULL,
    [S1RelationshipTypeInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1RelationshipTypeInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1RelationshipTypeUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1RelationshipTypeUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1RelationshipTypeDeleted] DATETIME2 NULL,
    [S1RelationshipTypeRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1RelationshipType_UPDATE]
ON [dbo].[S1RelationshipType]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1RelationshipTypeUpdated] = GETUTCDATE(),
        [S1RelationshipTypeUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1RelationshipType] t
    INNER JOIN inserted i ON t.[S1RelationshipTypeId] = i.[S1RelationshipTypeId];
END
GO
