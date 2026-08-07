-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1MessagingMessageRecipient
-- Description: Message recipients
-- =============================================
CREATE TABLE [dbo].[S1MessagingMessageRecipient]
(
    [S1MessagingMessageRecipientId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    [S1MessagingMessageRecipientIdentity] INT IDENTITY(0,1) NOT NULL,
    [S1MessagingMessageId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[S1MessagingMessage]([S1MessagingMessageId]),
    [Recipient] VARCHAR(256) NOT NULL,
    [RecipientDisplayName] NVARCHAR(256) NULL,
    [RecipientType] VARCHAR(16) NOT NULL DEFAULT 'TO',
    [DeliveredAt] DATETIME2 NULL,
    [S1MessagingMessageRecipientInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1MessagingMessageRecipientInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1MessagingMessageRecipientUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1MessagingMessageRecipientUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1MessagingMessageRecipientDeleted] DATETIME2 NULL,
    [S1MessagingMessageRecipientRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1MessagingMessageRecipient_UPDATE]
ON [dbo].[S1MessagingMessageRecipient]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1MessagingMessageRecipientUpdated] = GETUTCDATE(),
        [S1MessagingMessageRecipientUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1MessagingMessageRecipient] t
    INNER JOIN inserted i ON t.[S1MessagingMessageRecipientId] = i.[S1MessagingMessageRecipientId];
END
GO
