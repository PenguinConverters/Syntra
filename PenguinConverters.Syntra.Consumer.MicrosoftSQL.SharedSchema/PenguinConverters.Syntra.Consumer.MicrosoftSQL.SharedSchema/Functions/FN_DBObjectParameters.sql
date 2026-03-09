-- =============================================
-- Syntra - Penguin Converters AG
-- Function: FN_DBObjectParameters
-- Description: Returns stored procedure parameters with metadata
-- =============================================
CREATE FUNCTION [dbo].[FN_DBObjectParameters]
(
    @ObjectName NVARCHAR(256)
)
RETURNS @Parameters TABLE
(
    [Name] NVARCHAR(128),
    [SystemTypeName] NVARCHAR(128),
    [MaxLength] SMALLINT,
    [IsNullable] BIT,
    [HasDefaultValue] BIT,
    [OrdinalPosition] INT
)
AS
BEGIN
    INSERT INTO @Parameters ([Name], [SystemTypeName], [MaxLength], [IsNullable], [HasDefaultValue], [OrdinalPosition])
    SELECT
        p.[name] AS [Name],
        TYPE_NAME(p.[system_type_id]) AS [SystemTypeName],
        p.[max_length] AS [MaxLength],
        CASE WHEN p.[is_nullable] = 1 THEN 1 ELSE 0 END AS [IsNullable],
        p.[has_default_value] AS [HasDefaultValue],
        p.[parameter_id] AS [OrdinalPosition]
    FROM sys.parameters p
    INNER JOIN sys.objects o ON p.[object_id] = o.[object_id]
    WHERE o.[name] = @ObjectName
      AND p.[parameter_id] > 0
    ORDER BY p.[parameter_id];

    RETURN;
END
GO
