-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1TransactionResult
-- Description: Transaction execution results
-- =============================================
CREATE TABLE [dbo].[S1TransactionResult]
(
    [S1TransactionResultId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1TransactionResultIdentity] INT IDENTITY(0,1) NOT NULL,
    [S1TransactionId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[S1Transaction]([S1TransactionId]),
    [Completed] BIT NOT NULL DEFAULT 0,
    [HasError] BIT NOT NULL DEFAULT 0,
    [Message] NVARCHAR(MAX) NULL,
    [S1TransactionResultInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TransactionResultInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TransactionResultUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TransactionResultUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TransactionResultDeleted] DATETIME2 NULL,
    [S1TransactionResultRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1TransactionResult_UPDATE]
ON [dbo].[S1TransactionResult]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1TransactionResultUpdated] = GETUTCDATE(),
        [S1TransactionResultUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1TransactionResult] t
    INNER JOIN inserted i ON t.[S1TransactionResultId] = i.[S1TransactionResultId];
END
GO
