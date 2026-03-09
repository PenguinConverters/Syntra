-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Messaging
-- Description: Messaging channel definitions
-- =============================================
CREATE TABLE [dbo].[S1Messaging]
(
    [S1MessagingId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1MessagingIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(128) NOT NULL,
    [DisplayName] NVARCHAR(256) NULL,
    [Description] NVARCHAR(512) NULL,
    [S1MessagingInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1MessagingInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1MessagingUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1MessagingUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1MessagingDeleted] DATETIME2 NULL,
    [S1MessagingRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1Messaging_UPDATE]
ON [dbo].[S1Messaging]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1MessagingUpdated] = GETUTCDATE(),
        [S1MessagingUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Messaging] t
    INNER JOIN inserted i ON t.[S1MessagingId] = i.[S1MessagingId];
END
GO
