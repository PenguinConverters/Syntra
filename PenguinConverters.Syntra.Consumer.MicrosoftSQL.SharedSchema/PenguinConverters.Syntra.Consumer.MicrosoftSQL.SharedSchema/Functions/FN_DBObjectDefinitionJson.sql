-- =============================================
-- Syntra - Penguin Converters AG
-- Function: FN_DBObjectDefinitionJson
-- Description: Returns JSON definition of a DB object including parameters, types, maxLength, displayName, FK relationships
-- =============================================
CREATE FUNCTION [dbo].[FN_DBObjectDefinitionJson]
(
    @ObjectName NVARCHAR(256)
)
RETURNS NVARCHAR(MAX)
AS
BEGIN
    DECLARE @Result NVARCHAR(MAX);

    SELECT @Result = (
        SELECT
            o.[name] AS [objectName],
            o.[type_desc] AS [objectType],
            (
                SELECT
                    p.[name] AS [name],
                    TYPE_NAME(p.[system_type_id]) AS [type],
                    p.[max_length] AS [maxLength],
                    CASE WHEN p.[is_nullable] = 1 THEN 'true' ELSE 'false' END AS [isNullable],
                    p.[has_default_value] AS [hasDefaultValue],
                    REPLACE(REPLACE(p.[name], '@', ''), 'Id', '') AS [displayName],
                    (
                        SELECT TOP 1
                            fk_tab.[name] AS [referencedTable],
                            fk_col.[name] AS [referencedColumn]
                        FROM sys.foreign_key_columns fkc
                        INNER JOIN sys.tables fk_tab ON fkc.[referenced_object_id] = fk_tab.[object_id]
                        INNER JOIN sys.columns fk_col ON fkc.[referenced_object_id] = fk_col.[object_id]
                            AND fkc.[referenced_column_id] = fk_col.[column_id]
                        INNER JOIN sys.columns parent_col ON fkc.[parent_object_id] = parent_col.[object_id]
                            AND fkc.[parent_column_id] = parent_col.[column_id]
                        WHERE parent_col.[name] = REPLACE(p.[name], '@', '')
                        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                    ) AS [foreignKey]
                FROM sys.parameters p
                WHERE p.[object_id] = o.[object_id]
                  AND p.[parameter_id] > 0
                ORDER BY p.[parameter_id]
                FOR JSON PATH
            ) AS [parameters]
        FROM sys.objects o
        WHERE o.[name] = @ObjectName
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    );

    RETURN @Result;
END
GO
