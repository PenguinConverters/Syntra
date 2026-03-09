-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Node
-- Description: Discovery graph nodes
-- =============================================
CREATE TABLE [dbo].[S1Node]
(
    [S1NodeId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1NodeIdentity] INT IDENTITY(0,1) NOT NULL,
    [NodeId] VARCHAR(128) NOT NULL,
    [Identifier] VARCHAR(256) NOT NULL,
    [DisplayName] NVARCHAR(256) NULL,
    [TableName] VARCHAR(128) NULL,
    [Type] VARCHAR(64) NULL,
    [CFactor] TINYINT NOT NULL DEFAULT 0,
    [CFactorInherited] TINYINT NOT NULL DEFAULT 0,
    [AFactor] TINYINT NOT NULL DEFAULT 0,
    [AFactorInherited] TINYINT NOT NULL DEFAULT 0,
    [S1NodeInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1NodeInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1NodeUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1NodeUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1NodeDeleted] DATETIME2 NULL,
    [S1NodeRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1Node_UPDATE]
ON [dbo].[S1Node]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1NodeUpdated] = GETUTCDATE(),
        [S1NodeUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Node] t
    INNER JOIN inserted i ON t.[S1NodeId] = i.[S1NodeId];
END
GO
