-- =============================================
-- Syntra - Penguin Converters AG
-- Stored Procedure: SP_S1Transaction_CREATE
-- Description: Creates transaction records with optional approval workflow integration
-- =============================================
CREATE PROCEDURE [dbo].[SP_S1Transaction_CREATE]
    @S1TransactionOperationId UNIQUEIDENTIFIER,
    @S1TransactionPredecessorId UNIQUEIDENTIFIER = NULL,
    @S1ApprovalId UNIQUEIDENTIFIER = NULL,
    @S1TransactionRequestId UNIQUEIDENTIFIER = NULL,
    @RunOnPredecessorError BIT = 0,
    @ObjectId01 VARCHAR(128) = NULL,
    @ObjectId02 VARCHAR(128) = NULL,
    @ObjectId03 VARCHAR(128) = NULL,
    @ObjectId04 VARCHAR(256) = NULL,
    @PropertyJson01 VARCHAR(MAX) = NULL,
    @PropertyJson02 VARCHAR(MAX) = NULL,
    @PropertyJson03 VARCHAR(MAX) = NULL,
    @PropertyJson04 VARCHAR(MAX) = NULL,
    @PropertyText01 NVARCHAR(MAX) = NULL,
    @PropertyText02 NVARCHAR(MAX) = NULL,
    @PropertyHtml01 VARCHAR(MAX) = NULL,
    @S1TransactionWorkflowIdentifier VARCHAR(33) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- NEWID() rather than NEWSEQUENTIALID(): the latter is only legal as a DEFAULT
    -- constraint on a UNIQUEIDENTIFIER column and cannot be called in an expression.
    -- The id is generated here so it can be returned to the caller, which means rows
    -- created through this procedure do not benefit from the sequential default on
    -- [S1Transaction].[S1TransactionId].
    DECLARE @S1TransactionId UNIQUEIDENTIFIER = NEWID();

    -- Validate the operation exists
    IF NOT EXISTS (SELECT 1 FROM [dbo].[S1TransactionOperation] WHERE [S1TransactionOperationId] = @S1TransactionOperationId)
    BEGIN
        RAISERROR('S1TransactionOperation not found.', 16, 1);
        RETURN;
    END

    -- If approval is specified, validate it exists
    IF @S1ApprovalId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[S1Approval] WHERE [S1ApprovalId] = @S1ApprovalId)
    BEGIN
        RAISERROR('S1Approval not found.', 16, 1);
        RETURN;
    END

    -- If predecessor is specified, validate it exists
    IF @S1TransactionPredecessorId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[S1Transaction] WHERE [S1TransactionId] = @S1TransactionPredecessorId)
    BEGIN
        RAISERROR('S1Transaction predecessor not found.', 16, 1);
        RETURN;
    END

    INSERT INTO [dbo].[S1Transaction]
    (
        [S1TransactionId],
        [S1TransactionPredecessorId],
        [S1TransactionOperationId],
        [S1ApprovalId],
        [S1TransactionRequestId],
        [RunOnPredecessorError],
        [ObjectId01],
        [ObjectId02],
        [ObjectId03],
        [ObjectId04],
        [PropertyJson01],
        [PropertyJson02],
        [PropertyJson03],
        [PropertyJson04],
        [PropertyText01],
        [PropertyText02],
        [PropertyHtml01],
        [S1TransactionWorkflowIdentifier]
    )
    VALUES
    (
        @S1TransactionId,
        @S1TransactionPredecessorId,
        @S1TransactionOperationId,
        @S1ApprovalId,
        @S1TransactionRequestId,
        @RunOnPredecessorError,
        @ObjectId01,
        @ObjectId02,
        @ObjectId03,
        @ObjectId04,
        @PropertyJson01,
        @PropertyJson02,
        @PropertyJson03,
        @PropertyJson04,
        @PropertyText01,
        @PropertyText02,
        @PropertyHtml01,
        @S1TransactionWorkflowIdentifier
    );

    -- If an approval workflow is configured, create the initial approval request
    IF @S1ApprovalId IS NOT NULL
    BEGIN
        DECLARE @NewStateId UNIQUEIDENTIFIER;
        SELECT @NewStateId = [S1ApprovalStateId] FROM [dbo].[S1ApprovalState] WHERE [Name] = 'New';

        IF @NewStateId IS NOT NULL
        BEGIN
            INSERT INTO [dbo].[S1ApprovalRequest] ([S1TransactionId], [S1ApprovalStateId], [Requester], [RequesterDisplayName])
            VALUES (@S1TransactionId, @NewStateId, SUSER_SNAME(), SUSER_SNAME());
        END
    END

    SELECT @S1TransactionId AS [S1TransactionId];
END
GO
