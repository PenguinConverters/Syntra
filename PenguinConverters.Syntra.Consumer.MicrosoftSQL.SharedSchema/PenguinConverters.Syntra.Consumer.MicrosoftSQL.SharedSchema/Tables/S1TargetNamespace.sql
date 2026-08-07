-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1TargetNamespace
-- Description: Tenant/namespace definitions
-- =============================================
CREATE TABLE [dbo].[S1TargetNamespace]
(
    [S1TargetNamespaceId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    [S1TargetNamespaceIdentity] INT IDENTITY(0,1) NOT NULL,
    [Name] VARCHAR(128) NOT NULL,
    [DisplayName] NVARCHAR(256) NULL,
    [Identifier] CHAR(2) NOT NULL,
    [S1TargetId] UNIQUEIDENTIFIER NULL REFERENCES [dbo].[S1Target]([S1TargetId]),
    [S1TargetNamespaceInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TargetNamespaceInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TargetNamespaceUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1TargetNamespaceUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1TargetNamespaceDeleted] DATETIME2 NULL,
    [S1TargetNamespaceRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1TargetNamespace_UPDATE]
ON [dbo].[S1TargetNamespace]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1TargetNamespaceUpdated] = GETUTCDATE(),
        [S1TargetNamespaceUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1TargetNamespace] t
    INNER JOIN inserted i ON t.[S1TargetNamespaceId] = i.[S1TargetNamespaceId];
END
GO
