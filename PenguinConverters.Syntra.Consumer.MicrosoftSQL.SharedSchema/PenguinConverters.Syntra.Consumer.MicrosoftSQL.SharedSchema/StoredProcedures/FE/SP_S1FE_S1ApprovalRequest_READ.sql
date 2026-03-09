-- =============================================
-- Syntra - Penguin Converters AG
-- Stored Procedure: SP_S1FE_S1ApprovalRequest_READ
-- Description: Approval request read with metadata support
-- <!-- @DisplayName: Approval Requests -->
-- <!-- @Category: Approvals -->
-- <!-- @Icon: check-circle -->
-- =============================================
CREATE PROCEDURE [dbo].[SP_S1FE_S1ApprovalRequest_READ]
    @S1ApprovalRequestId UNIQUEIDENTIFIER = NULL,
    @metadata INT = 0,
    @top INT = 100,
    @skip INT = 0,
    @approver VARCHAR(128) = NULL,
    @S1ApprovalStateId UNIQUEIDENTIFIER = NULL,
    @orderby NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Mode 4: Parameters
    IF @metadata = 4
    BEGIN
        SELECT * FROM [dbo].[FN_DBObjectParameters]('SP_S1FE_S1ApprovalRequest_READ');
        RETURN;
    END

    -- Mode 8: Field definitions
    IF @metadata = 8
    BEGIN
        SELECT
            c.[name] AS [ColumnName],
            TYPE_NAME(c.[system_type_id]) AS [DataType],
            c.[max_length] AS [MaxLength],
            CASE c.[name]
                WHEN 'S1ApprovalRequestId' THEN 'Id'
                WHEN 'S1TransactionId' THEN 'Transaction'
                WHEN 'S1ApprovalStateId' THEN 'State'
                WHEN 'ObjectId' THEN 'Object'
                WHEN 'SubjectId' THEN 'Subject'
                WHEN 'EntitlementId' THEN 'Entitlement'
                WHEN 'ApproverDisplayName' THEN 'Approver'
                WHEN 'RequesterDisplayName' THEN 'Requester'
                WHEN 'DecidedBy' THEN 'Decided By'
                WHEN 'DecidedAt' THEN 'Decided At'
                ELSE REPLACE(REPLACE(c.[name], 'S1ApprovalRequest', ''), 'S1', '')
            END AS [DisplayName],
            '.*' AS [RegularExpression],
            CASE
                WHEN c.[name] IN ('S1ApprovalRequestId', 'S1ApprovalStateId', 'Approver', 'Requester', 'DecidedBy') THEN 1
                ELSE 0
            END AS [FilterBy]
        FROM sys.columns c
        WHERE c.[object_id] = OBJECT_ID('S1ApprovalRequest')
        ORDER BY c.[column_id];
        RETURN;
    END

    -- Mode 3: Count
    IF @metadata = 3
    BEGIN
        SELECT COUNT(*) AS [Count]
        FROM [dbo].[S1ApprovalRequest]
        WHERE [S1ApprovalRequestDeleted] IS NULL
          AND (@approver IS NULL OR [Approver] = @approver)
          AND (@S1ApprovalStateId IS NULL OR [S1ApprovalStateId] = @S1ApprovalStateId);
        RETURN;
    END

    -- Mode 1: Minimal
    IF @metadata = 1
    BEGIN
        SELECT
            ar.[S1ApprovalRequestId] AS [Id],
            COALESCE(ar.[ApproverDisplayName], ar.[Approver], '') + ' - ' + s.[Name] AS [DisplayName]
        FROM [dbo].[S1ApprovalRequest] ar
        INNER JOIN [dbo].[S1ApprovalState] s ON ar.[S1ApprovalStateId] = s.[S1ApprovalStateId]
        WHERE ar.[S1ApprovalRequestDeleted] IS NULL
          AND (@S1ApprovalRequestId IS NULL OR ar.[S1ApprovalRequestId] = @S1ApprovalRequestId);
        RETURN;
    END

    -- Mode 0: Full data
    SELECT
        ar.[S1ApprovalRequestId],
        ar.[S1ApprovalRequestIdentity],
        ar.[S1TransactionId],
        ar.[S1ApprovalStateId],
        s.[Name] AS [ApprovalStateName],
        s.[DisplayName] AS [ApprovalStateDisplayName],
        ar.[ObjectId],
        ar.[ObjectTableId],
        ar.[SubjectId],
        ar.[SubjectTableId],
        ar.[EntitlementId],
        ar.[EntitlementTableId],
        ar.[Approver],
        ar.[ApproverDisplayName],
        ar.[Requester],
        ar.[RequesterDisplayName],
        ar.[DecidedBy],
        ar.[DecidedAt],
        ar.[S1ApprovalRequestInserted],
        ar.[S1ApprovalRequestInsertedBy],
        ar.[S1ApprovalRequestUpdated],
        ar.[S1ApprovalRequestUpdatedBy]
    FROM [dbo].[S1ApprovalRequest] ar
    INNER JOIN [dbo].[S1ApprovalState] s ON ar.[S1ApprovalStateId] = s.[S1ApprovalStateId]
    WHERE ar.[S1ApprovalRequestDeleted] IS NULL
      AND (@S1ApprovalRequestId IS NULL OR ar.[S1ApprovalRequestId] = @S1ApprovalRequestId)
      AND (@approver IS NULL OR ar.[Approver] = @approver)
      AND (@S1ApprovalStateId IS NULL OR ar.[S1ApprovalStateId] = @S1ApprovalStateId)
    ORDER BY
        CASE WHEN @orderby IS NULL THEN ar.[S1ApprovalRequestInserted] END DESC
    OFFSET @skip ROWS FETCH NEXT @top ROWS ONLY;
END
GO
