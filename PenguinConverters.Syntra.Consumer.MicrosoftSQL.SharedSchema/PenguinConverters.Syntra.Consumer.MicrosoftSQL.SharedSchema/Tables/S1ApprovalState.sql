-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1ApprovalState
-- Description: Approval state enum (New, Approved, Rejected, Expired, Cancelled)
-- =============================================
CREATE TABLE [dbo].[S1ApprovalState]
(
    [S1ApprovalStateId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    [S1ApprovalStateIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(64) NOT NULL,
    [DisplayName] NVARCHAR(128) NULL,
    [S1ApprovalStateInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1ApprovalStateInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1ApprovalStateUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1ApprovalStateUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1ApprovalStateDeleted] DATETIME2 NULL,
    [S1ApprovalStateRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1ApprovalState_UPDATE]
ON [dbo].[S1ApprovalState]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1ApprovalStateUpdated] = GETUTCDATE(),
        [S1ApprovalStateUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1ApprovalState] t
    INNER JOIN inserted i ON t.[S1ApprovalStateId] = i.[S1ApprovalStateId];
END
GO
