-- =============================================
-- Syntra - Penguin Converters AG
-- Stored Procedure: SP_S1FE_S1_READ
-- Description: Core read procedure with @metadata parameter
--   0 = data (full result set)
--   1 = minimal (Id and DisplayName only)
--   2 = properties (column metadata)
--   3 = count (total row count)
--   4 = parameters (procedure parameter definitions)
--   8 = field definitions (DataType, DisplayName, RegularExpression, FilterBy)
-- <!-- @DisplayName: Syntra Core Read -->
-- <!-- @Category: Core -->
-- =============================================
CREATE PROCEDURE [dbo].[SP_S1FE_S1_READ]
    @tableName VARCHAR(128),
    @id UNIQUEIDENTIFIER = NULL,
    @metadata INT = 0,
    @top INT = 100,
    @skip INT = 0,
    @filter NVARCHAR(MAX) = NULL,
    @orderby NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @sql NVARCHAR(MAX);
    DECLARE @pkColumn NVARCHAR(256);
    DECLARE @objectId INT;

    -- Resolve the table
    SET @objectId = OBJECT_ID(@tableName);
    IF @objectId IS NULL
    BEGIN
        RAISERROR('Table not found: %s', 16, 1, @tableName);
        RETURN;
    END

    -- Determine PK column name
    SELECT TOP 1 @pkColumn = c.[name]
    FROM sys.index_columns ic
    INNER JOIN sys.columns c ON ic.[object_id] = c.[object_id] AND ic.[column_id] = c.[column_id]
    INNER JOIN sys.indexes i ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
    WHERE i.[is_primary_key] = 1 AND ic.[object_id] = @objectId;

    -- Mode 1: Minimal (Id + DisplayName)
    IF @metadata = 1
    BEGIN
        SET @sql = N'SELECT [' + @pkColumn + N'] AS [Id]';

        IF COL_LENGTH(@tableName, 'DisplayName') IS NOT NULL
            SET @sql = @sql + N', [DisplayName]';
        ELSE IF COL_LENGTH(@tableName, 'Name') IS NOT NULL
            SET @sql = @sql + N', [Name] AS [DisplayName]';

        SET @sql = @sql + N' FROM [dbo].[' + @tableName + N']'
            + N' WHERE COALESCE([' + REPLACE(@tableName, 'dbo.', '') + N'Deleted], ''9999-12-31'') = ''9999-12-31''';

        IF @id IS NOT NULL
            SET @sql = @sql + N' AND [' + @pkColumn + N'] = @id';

        EXEC sp_executesql @sql, N'@id UNIQUEIDENTIFIER', @id;
        RETURN;
    END

    -- Mode 2: Properties (column metadata)
    IF @metadata = 2
    BEGIN
        SELECT
            c.[name] AS [ColumnName],
            TYPE_NAME(c.[system_type_id]) AS [DataType],
            c.[max_length] AS [MaxLength],
            c.[is_nullable] AS [IsNullable],
            c.[is_identity] AS [IsIdentity]
        FROM sys.columns c
        WHERE c.[object_id] = @objectId
        ORDER BY c.[column_id];
        RETURN;
    END

    -- Mode 3: Count
    IF @metadata = 3
    BEGIN
        SET @sql = N'SELECT COUNT(*) AS [Count] FROM [dbo].[' + @tableName + N']';
        EXEC sp_executesql @sql;
        RETURN;
    END

    -- Mode 4: Parameters
    IF @metadata = 4
    BEGIN
        SELECT * FROM [dbo].[FN_DBObjectParameters]('SP_S1FE_S1_READ');
        RETURN;
    END

    -- Mode 8: Field definitions with DataType, DisplayName, RegularExpression, FilterBy
    IF @metadata = 8
    BEGIN
        SELECT
            c.[name] AS [ColumnName],
            TYPE_NAME(c.[system_type_id]) AS [DataType],
            c.[max_length] AS [MaxLength],
            REPLACE(REPLACE(c.[name], @tableName, ''), 'Id', '') AS [DisplayName],
            CASE
                WHEN TYPE_NAME(c.[system_type_id]) IN ('varchar', 'nvarchar', 'char', 'nchar') THEN '.*'
                WHEN TYPE_NAME(c.[system_type_id]) IN ('int', 'bigint', 'smallint', 'tinyint') THEN '^[0-9]+$'
                WHEN TYPE_NAME(c.[system_type_id]) = 'uniqueidentifier' THEN '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$'
                WHEN TYPE_NAME(c.[system_type_id]) = 'bit' THEN '^(0|1|true|false)$'
                ELSE '.*'
            END AS [RegularExpression],
            CASE
                WHEN c.[is_identity] = 1 THEN 0
                WHEN c.[name] LIKE '%RowVersion%' THEN 0
                WHEN c.[name] LIKE '%Inserted%' THEN 0
                WHEN c.[name] LIKE '%Updated%' THEN 0
                WHEN c.[name] LIKE '%Deleted%' THEN 0
                ELSE 1
            END AS [FilterBy]
        FROM sys.columns c
        WHERE c.[object_id] = @objectId
        ORDER BY c.[column_id];
        RETURN;
    END

    -- Mode 0: Full data (default)
    SET @sql = N'SELECT * FROM [dbo].[' + @tableName + N']'
        + N' WHERE COALESCE([' + REPLACE(@tableName, 'dbo.', '') + N'Deleted], ''9999-12-31'') = ''9999-12-31''';

    IF @id IS NOT NULL
        SET @sql = @sql + N' AND [' + @pkColumn + N'] = @id';

    IF @filter IS NOT NULL
        SET @sql = @sql + N' AND ' + @filter;

    IF @orderby IS NOT NULL
        SET @sql = @sql + N' ORDER BY ' + @orderby;
    ELSE
        SET @sql = @sql + N' ORDER BY [' + @pkColumn + N']';

    SET @sql = @sql + N' OFFSET @skip ROWS FETCH NEXT @top ROWS ONLY';

    EXEC sp_executesql @sql, N'@id UNIQUEIDENTIFIER, @top INT, @skip INT', @id, @top, @skip;
END
GO
