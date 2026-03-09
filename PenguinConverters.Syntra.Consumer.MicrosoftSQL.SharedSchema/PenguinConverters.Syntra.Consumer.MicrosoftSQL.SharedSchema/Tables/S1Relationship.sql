-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Relationship
-- Description: Generic relationships between entities
-- =============================================
CREATE TABLE [dbo].[S1Relationship]
(
    [S1RelationshipId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1RelationshipIdentity] INT IDENTITY(0,1) NOT NULL,
    [SubjectId] VARCHAR(128) NOT NULL,
    [SubjectTableId] UNIQUEIDENTIFIER NOT NULL,
    [ObjectId] VARCHAR(128) NOT NULL,
    [ObjectTableId] UNIQUEIDENTIFIER NOT NULL,
    [S1RelationshipTypeId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[S1RelationshipType]([S1RelationshipTypeId]),
    [S1TargetNamespaceId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1TargetNamespace]([S1TargetNamespaceId]),
    [S1RelationshipInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1RelationshipInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1RelationshipUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1RelationshipUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1RelationshipDeleted] DATETIME2 NULL,
    [S1RelationshipRowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [UQ_S1Relationship] UNIQUE ([SubjectId], [SubjectTableId], [ObjectId], [ObjectTableId], [S1RelationshipTypeId])
)
GO

CREATE TRIGGER [dbo].[TR_S1Relationship_UPDATE]
ON [dbo].[S1Relationship]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1RelationshipUpdated] = GETUTCDATE(),
        [S1RelationshipUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Relationship] t
    INNER JOIN inserted i ON t.[S1RelationshipId] = i.[S1RelationshipId];
END
GO
