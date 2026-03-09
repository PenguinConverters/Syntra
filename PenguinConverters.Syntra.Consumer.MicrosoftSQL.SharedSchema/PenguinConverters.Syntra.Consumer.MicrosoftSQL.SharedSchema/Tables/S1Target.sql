-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Target
-- Description: Target system definitions
-- =============================================
CREATE TABLE [dbo].[S1Target]
(
    [S1TargetId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1TargetIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(128) NOT NULL,
    [DisplayName] NVARCHAR(256) NULL,
    [URL] VARCHAR(512) NULL,
    [S1TargetTypeId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1TargetType]([S1TargetTypeId]),
    [KeyVaultName] VARCHAR(128) NULL,
    [KeyVaultSecretName] VARCHAR(128) NULL,
    [S1TargetInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TargetInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TargetUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TargetUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TargetDeleted] DATETIME2 NULL,
    [S1TargetRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1Target_UPDATE]
ON [dbo].[S1Target]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1TargetUpdated] = GETUTCDATE(),
        [S1TargetUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Target] t
    INNER JOIN inserted i ON t.[S1TargetId] = i.[S1TargetId];
END
GO
