-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Transaction
-- Description: Transaction workflow records
-- =============================================
CREATE TABLE [dbo].[S1Transaction]
(
    [S1TransactionId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1TransactionIdentity] INT IDENTITY(0,1) NOT NULL,
    [S1TransactionPredecessorId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1Transaction]([S1TransactionId]),
    [S1TransactionOperationId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[S1TransactionOperation]([S1TransactionOperationId]),
    [S1ApprovalId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1Approval]([S1ApprovalId]),
    [S1TransactionRequestId] UNIQUEIDENTIFIER NULL,
    [RunOnPredecessorError] BIT NOT NULL DEFAULT 0,
    [ObjectId01] VARCHAR(128) NULL,
    [ObjectId02] VARCHAR(128) NULL,
    [ObjectId03] VARCHAR(128) NULL,
    [ObjectId04] VARCHAR(256) NULL,
    [PropertyJson01] VARCHAR(MAX) NULL,
    [PropertyJson02] VARCHAR(MAX) NULL,
    [PropertyJson03] VARCHAR(MAX) NULL,
    [PropertyJson04] VARCHAR(MAX) NULL,
    [PropertyText01] NVARCHAR(MAX) NULL,
    [PropertyText02] NVARCHAR(MAX) NULL,
    [PropertyHtml01] VARCHAR(MAX) NULL,
    [S1TransactionWorkflowIdentifier] VARCHAR(33) NULL,
    [S1TransactionInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TransactionInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TransactionUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TransactionUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TransactionDeleted] DATETIME2 NULL,
    [S1TransactionRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1Transaction_UPDATE]
ON [dbo].[S1Transaction]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1TransactionUpdated] = GETUTCDATE(),
        [S1TransactionUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Transaction] t
    INNER JOIN inserted i ON t.[S1TransactionId] = i.[S1TransactionId];
END
GO
