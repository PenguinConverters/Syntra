-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Streamer
-- Description: Stored SQL queries for streaming
-- =============================================
CREATE TABLE [dbo].[S1Streamer]
(
    [S1StreamerId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    [S1StreamerIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(128) NOT NULL,
    [Description] NVARCHAR(512) NULL,
    [SQL] NVARCHAR(MAX) NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [S1StreamerInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1StreamerInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1StreamerUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1StreamerUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1StreamerDeleted] DATETIME2 NULL,
    [S1StreamerRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1Streamer_UPDATE]
ON [dbo].[S1Streamer]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1StreamerUpdated] = GETUTCDATE(),
        [S1StreamerUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Streamer] t
    INNER JOIN inserted i ON t.[S1StreamerId] = i.[S1StreamerId];
END
GO
