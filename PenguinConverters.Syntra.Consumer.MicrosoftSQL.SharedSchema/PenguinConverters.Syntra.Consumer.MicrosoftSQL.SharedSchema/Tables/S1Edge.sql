-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Edge
-- Description: Security graph edges
-- =============================================
CREATE TABLE [dbo].[S1Edge]
(
    [S1EdgeId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1EdgeIdentity] INT IDENTITY(0,1) NOT NULL,
    [NodeToIdentifier] VARCHAR(256) NOT NULL,
    [NodeFromIdentifier] VARCHAR(256) NOT NULL,
    [TableName] VARCHAR(128) NULL,
    [Type] VARCHAR(64) NULL,
    [S1EdgeTypeId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1EdgeType]([S1EdgeTypeId]),
    [S1EdgeInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1EdgeInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1EdgeUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1EdgeUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1EdgeDeleted] DATETIME2 NULL,
    [S1EdgeRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1Edge_UPDATE]
ON [dbo].[S1Edge]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1EdgeUpdated] = GETUTCDATE(),
        [S1EdgeUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Edge] t
    INNER JOIN inserted i ON t.[S1EdgeId] = i.[S1EdgeId];
END
GO
