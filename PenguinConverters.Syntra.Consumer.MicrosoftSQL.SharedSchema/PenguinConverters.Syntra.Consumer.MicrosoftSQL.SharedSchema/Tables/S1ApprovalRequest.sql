-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1ApprovalRequest
-- Description: Approval request instances
-- =============================================
CREATE TABLE [dbo].[S1ApprovalRequest]
(
    [S1ApprovalRequestId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1ApprovalRequestIdentity] INT IDENTITY(0,1) NOT NULL,
    [S1TransactionId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[S1Transaction]([S1TransactionId]),
    [S1ApprovalStateId] UNIQUEIDENTIFIER NOT NULL REFERENCES [dbo].[S1ApprovalState]([S1ApprovalStateId]),
    [ObjectId] VARCHAR(128) NULL,
    [ObjectTableId] UNIQUEIDENTIFIER NULL,
    [SubjectId] VARCHAR(128) NULL,
    [SubjectTableId] UNIQUEIDENTIFIER NULL,
    [EntitlementId] VARCHAR(128) NULL,
    [EntitlementTableId] UNIQUEIDENTIFIER NULL,
    [Approver] VARCHAR(128) NULL,
    [ApproverDisplayName] NVARCHAR(256) NULL,
    [Requester] VARCHAR(128) NULL,
    [RequesterDisplayName] NVARCHAR(256) NULL,
    [DecidedBy] VARCHAR(128) NULL,
    [DecidedAt] DATETIME2 NULL,
    [S1ApprovalRequestInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1ApprovalRequestInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1ApprovalRequestUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1ApprovalRequestUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1ApprovalRequestDeleted] DATETIME2 NULL,
    [S1ApprovalRequestRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1ApprovalRequest_UPDATE]
ON [dbo].[S1ApprovalRequest]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1ApprovalRequestUpdated] = GETUTCDATE(),
        [S1ApprovalRequestUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1ApprovalRequest] t
    INNER JOIN inserted i ON t.[S1ApprovalRequestId] = i.[S1ApprovalRequestId];
END
GO
