-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Shadow
-- Description: JSON shadow copies of entity state
-- =============================================
CREATE TABLE [dbo].[S1Shadow]
(
    [S1ShadowId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1ShadowIdentity] INT IDENTITY(0,1) NOT NULL,
    [S1TableId] UNIQUEIDENTIFIER NOT NULL,
    [ObjectId] VARCHAR(128) NOT NULL,
    [ShadowJson] VARCHAR(MAX) NULL,
    [S1ShadowInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1ShadowInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1ShadowUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1ShadowUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1ShadowDeleted] DATETIME2 NULL,
    [S1ShadowRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1Shadow_UPDATE]
ON [dbo].[S1Shadow]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1ShadowUpdated] = GETUTCDATE(),
        [S1ShadowUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Shadow] t
    INNER JOIN inserted i ON t.[S1ShadowId] = i.[S1ShadowId];
END
GO
