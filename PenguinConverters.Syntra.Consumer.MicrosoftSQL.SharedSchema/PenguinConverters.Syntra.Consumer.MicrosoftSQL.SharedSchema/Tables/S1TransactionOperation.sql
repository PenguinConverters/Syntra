-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1TransactionOperation
-- Description: Operation definitions for transactions
-- =============================================
CREATE TABLE [dbo].[S1TransactionOperation]
(
    [S1TransactionOperationId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1TransactionOperationIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(128) NOT NULL,
    [DisplayName] NVARCHAR(256) NULL,
    [URI] VARCHAR(512) NOT NULL,
    [Method] VARCHAR(16) NOT NULL,
    [S1TargetId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1Target]([S1TargetId]),
    [ManagedIdentity] BIT NOT NULL DEFAULT 0,
    [Retry] INT NOT NULL DEFAULT 0,
    [PropertyJson01] VARCHAR(MAX) NULL,
    [Request] BIT NOT NULL DEFAULT 0,
    [S1TransactionOperationInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TransactionOperationInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TransactionOperationUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TransactionOperationUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TransactionOperationDeleted] DATETIME2 NULL,
    [S1TransactionOperationRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1TransactionOperation_UPDATE]
ON [dbo].[S1TransactionOperation]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1TransactionOperationUpdated] = GETUTCDATE(),
        [S1TransactionOperationUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1TransactionOperation] t
    INNER JOIN inserted i ON t.[S1TransactionOperationId] = i.[S1TransactionOperationId];
END
GO
