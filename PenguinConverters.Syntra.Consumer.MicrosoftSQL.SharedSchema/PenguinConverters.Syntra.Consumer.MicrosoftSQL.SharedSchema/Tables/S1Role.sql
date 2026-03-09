-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Role
-- Description: Role definitions
-- =============================================
CREATE TABLE [dbo].[S1Role]
(
    [S1RoleId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1RoleIdentity] INT IDENTITY(0,1) NOT NULL,
    [Value] VARCHAR(256) NOT NULL,
    [Display] NVARCHAR(256) NULL,
    [Type] VARCHAR(64) NULL,
    [Enabled] BIT NOT NULL DEFAULT 1,
    [TotalAssignmentsPermitted] INT NULL,
    [S1RoleTypeId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1RoleType]([S1RoleTypeId]),
    [S1RoleInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1RoleInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1RoleUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1RoleUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1RoleDeleted] DATETIME2 NULL,
    [S1RoleRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1Role_UPDATE]
ON [dbo].[S1Role]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1RoleUpdated] = GETUTCDATE(),
        [S1RoleUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Role] t
    INNER JOIN inserted i ON t.[S1RoleId] = i.[S1RoleId];
END
GO
