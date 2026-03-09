-- =============================================
-- Syntra - Penguin Converters AG
-- Stored Procedure: SP_S1TransactionResult_CREATE
-- Description: Records transaction execution results
-- =============================================
CREATE PROCEDURE [dbo].[SP_S1TransactionResult_CREATE]
    @S1TransactionId UNIQUEIDENTIFIER,
    @Completed BIT = 0,
    @HasError BIT = 0,
    @Message NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Validate the transaction exists
    IF NOT EXISTS (SELECT 1 FROM [dbo].[S1Transaction] WHERE [S1TransactionId] = @S1TransactionId)
    BEGIN
        RAISERROR('S1Transaction not found.', 16, 1);
        RETURN;
    END

    DECLARE @S1TransactionResultId UNIQUEIDENTIFIER = NEWID();

    INSERT INTO [dbo].[S1TransactionResult]
    (
        [S1TransactionResultId],
        [S1TransactionId],
        [Completed],
        [HasError],
        [Message]
    )
    VALUES
    (
        @S1TransactionResultId,
        @S1TransactionId,
        @Completed,
        @HasError,
        @Message
    );

    -- If completed without error, check for successor transactions
    IF @Completed = 1 AND @HasError = 0
    BEGIN
        -- Return successor transactions that should be triggered
        SELECT [S1TransactionId], [S1TransactionOperationId]
        FROM [dbo].[S1Transaction]
        WHERE [S1TransactionPredecessorId] = @S1TransactionId
          AND [S1TransactionDeleted] IS NULL;
    END
    ELSE IF @HasError = 1
    BEGIN
        -- Return successor transactions that run on predecessor error
        SELECT [S1TransactionId], [S1TransactionOperationId]
        FROM [dbo].[S1Transaction]
        WHERE [S1TransactionPredecessorId] = @S1TransactionId
          AND [RunOnPredecessorError] = 1
          AND [S1TransactionDeleted] IS NULL;
    END

    SELECT @S1TransactionResultId AS [S1TransactionResultId];
END
GO
