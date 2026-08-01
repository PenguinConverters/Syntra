-- =============================================
-- Syntra - Penguin Converters AG
-- Table: S1Column
-- Description: Column metadata tracking for schema inference
-- =============================================
CREATE TABLE [dbo].[S1Column]
(
    [S1ColumnId] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    [S1ColumnIdentity] INT IDENTITY(0,1) NOT NULL,
    [TableName] VARCHAR(128) NOT NULL,
    [ColumnName] VARCHAR(128) NOT NULL,
    [DataType] VARCHAR(64) NULL,
    [MaxLength] INT NULL,
    [IsNullable] BIT NOT NULL DEFAULT 1,
    [DisplayName] NVARCHAR(256) NULL,
    [S1ColumnInserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1ColumnInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1ColumnUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [S1ColumnUpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
    [S1ColumnDeleted] DATETIME2 NULL,
    [S1ColumnRowVersion] ROWVERSION NOT NULL
)
GO

CREATE TRIGGER [dbo].[TR_S1Column_UPDATE]
ON [dbo].[S1Column]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
    SET [S1ColumnUpdated] = GETUTCDATE(),
        [S1ColumnUpdatedBy] = SUSER_SNAME()
    FROM [dbo].[S1Column] t
    INNER JOIN inserted i ON t.[S1ColumnId] = i.[S1ColumnId];
END
GO
