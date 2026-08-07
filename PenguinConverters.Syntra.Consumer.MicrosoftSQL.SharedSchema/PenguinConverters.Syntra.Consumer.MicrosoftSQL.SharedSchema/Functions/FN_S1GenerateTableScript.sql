-- =============================================
-- Syntra - Penguin Converters AG
-- Function: FN_S1GenerateTableScript
-- Description: Generates standard CREATE TABLE script with audit columns
-- =============================================
CREATE FUNCTION [dbo].[FN_S1GenerateTableScript]
(
    @TableName VARCHAR(128),
    @Prefix VARCHAR(4)
)
RETURNS NVARCHAR(MAX)
AS
BEGIN
    DECLARE @Script NVARCHAR(MAX);

    SET @Script = 'CREATE TABLE [dbo].[' + @TableName + ']' + CHAR(13) + CHAR(10)
        + '(' + CHAR(13) + CHAR(10)
        + '    [' + @Prefix + @TableName + 'Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,' + CHAR(13) + CHAR(10)
        + '    [' + @Prefix + @TableName + 'Identity] INT IDENTITY(0,1) NOT NULL,' + CHAR(13) + CHAR(10)
        + '    -- Add custom columns here' + CHAR(13) + CHAR(10)
        + '    [' + @Prefix + @TableName + 'Inserted] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),' + CHAR(13) + CHAR(10)
        + '    [' + @Prefix + @TableName + 'InsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),' + CHAR(13) + CHAR(10)
        + '    [' + @Prefix + @TableName + 'Updated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),' + CHAR(13) + CHAR(10)
        + '    [' + @Prefix + @TableName + 'UpdatedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),' + CHAR(13) + CHAR(10)
        + '    [' + @Prefix + @TableName + 'Deleted] DATETIME2 NULL,' + CHAR(13) + CHAR(10)
        + '    [' + @Prefix + @TableName + 'RowVersion] ROWVERSION NOT NULL' + CHAR(13) + CHAR(10)
        + ')';

    RETURN @Script;
END
GO
