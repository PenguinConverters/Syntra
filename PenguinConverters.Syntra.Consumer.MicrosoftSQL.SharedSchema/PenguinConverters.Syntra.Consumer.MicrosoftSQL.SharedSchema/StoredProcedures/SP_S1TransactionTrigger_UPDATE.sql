-- =============================================
-- Syntra - Penguin Converters AG
-- Stored Procedure: SP_S1TransactionTrigger_UPDATE
-- Description: Updates transaction trigger status for workflow progression
-- =============================================
CREATE PROCEDURE [dbo].[SP_S1TransactionTrigger_UPDATE]
    @S1TransactionId UNIQUEIDENTIFIER,
    @S1TransactionWorkflowIdentifier VARCHAR(33) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Validate the transaction exists
    IF NOT EXISTS (SELECT 1 FROM [dbo].[S1Transaction] WHERE [S1TransactionId] = @S1TransactionId)
    BEGIN
        RAISERROR('S1Transaction not found.', 16, 1);
        RETURN;
    END

    UPDATE [dbo].[S1Transaction]
    SET [S1TransactionWorkflowIdentifier] = ISNULL(@S1TransactionWorkflowIdentifier, [S1TransactionWorkflowIdentifier])
    WHERE [S1TransactionId] = @S1TransactionId;

    -- Return the updated transaction with its operation details
    SELECT
        t.[S1TransactionId],
        t.[S1TransactionOperationId],
        t.[S1TransactionPredecessorId],
        t.[S1TransactionWorkflowIdentifier],
        o.[Name] AS [OperationName],
        o.[URI] AS [OperationURI],
        o.[Method] AS [OperationMethod],
        o.[ManagedIdentity],
        o.[Retry],
        t.[ObjectId01],
        t.[ObjectId02],
        t.[ObjectId03],
        t.[ObjectId04],
        t.[PropertyJson01],
        t.[PropertyJson02],
        t.[PropertyJson03],
        t.[PropertyJson04]
    FROM [dbo].[S1Transaction] t
    INNER JOIN [dbo].[S1TransactionOperation] o ON t.[S1TransactionOperationId] = o.[S1TransactionOperationId]
    WHERE t.[S1TransactionId] = @S1TransactionId;
END
GO
