-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Approval
-- Description: Approval workflow definitions with predecessor chain
-- =============================================
CREATE TABLE [dbo].[S1Approval]
(
    [S1ApprovalId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1ApprovalIdentity] INT IDENTITY(0,1) NOT NULL,
    [S1ApprovalPredecessorId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1Approval]([S1ApprovalId]),
    [ApproverSelectionSql] NVARCHAR(MAX) NULL,
    [Name] VARCHAR(128) NOT NULL,
    [Description] NVARCHAR(512) NULL,
    [ApprovalsRequiredCount] INT NOT NULL DEFAULT 1,
    [CustomerRequestMessage] NVARCHAR(MAX) NULL,
    [ApprovalRejectedMessage] NVARCHAR(MAX) NULL,
    [ApprovalApprovedMessage] NVARCHAR(MAX) NULL,
    [ApproverApprovalMessage] NVARCHAR(MAX) NULL,
    [Headline] NVARCHAR(256) NULL,
    [S1ApprovalInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1ApprovalInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1ApprovalUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1ApprovalUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1ApprovalDeleted] DATETIME2 NULL,
    [S1ApprovalRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1Approval_UPDATE]
ON [dbo].[S1Approval]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1ApprovalUpdated] = GETUTCDATE(),
        [S1ApprovalUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Approval] t
    INNER JOIN inserted i ON t.[S1ApprovalId] = i.[S1ApprovalId];
END
GO
