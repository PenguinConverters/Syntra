-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1MessagingMessage
-- Description: Individual messages
-- =============================================
CREATE TABLE [dbo].[S1MessagingMessage]
(
    [S1MessagingMessageId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    [S1MessagingMessageIdentity] INT IDENTITY(0,1) NOT NULL,
    [S1MessagingId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[S1Messaging]([S1MessagingId]),
    [Subject] NVARCHAR(256) NULL,
    [Body] NVARCHAR(MAX) NULL,
    [BodyHtml] VARCHAR(MAX) NULL,
    [Priority] INT NOT NULL DEFAULT 0,
    [S1MessagingMessageInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1MessagingMessageInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1MessagingMessageUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1MessagingMessageUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1MessagingMessageDeleted] DATETIME2 NULL,
    [S1MessagingMessageRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1MessagingMessage_UPDATE]
ON [dbo].[S1MessagingMessage]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1MessagingMessageUpdated] = GETUTCDATE(),
        [S1MessagingMessageUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1MessagingMessage] t
    INNER JOIN inserted i ON t.[S1MessagingMessageId] = i.[S1MessagingMessageId];
END
GO
