-- =============================================
-- Syntra - Penguin Converters AG
-- Stored Procedure: SP_S1FE_S1Identity_READ
-- Description: Identity read with metadata support
-- <!-- @DisplayName: Identity Read -->
-- <!-- @Category: Identity -->
-- <!-- @Icon: person -->
-- =============================================
CREATE PROCEDURE [dbo].[SP_S1FE_S1Identity_READ]
    @S1IdentityId UNIQUEIDENTIFIER = NULL,
    @metadata INT = 0,
    @top INT = 100,
    @skip INT = 0,
    @filter NVARCHAR(MAX) = NULL,
    @orderby NVARCHAR(256) = NULL,
    @search NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Mode 4: Parameters
    IF @metadata = 4
    BEGIN
        SELECT * FROM [dbo].[FN_DBObjectParameters]('SP_S1FE_S1Identity_READ');
        RETURN;
    END

    -- Mode 8: Field definitions
    IF @metadata = 8
    BEGIN
        SELECT
            c.[name] AS [ColumnName],
            TYPE_NAME(c.[system_type_id]) AS [DataType],
            c.[max_length] AS [MaxLength],
            CASE c.[name]
                WHEN 'S1IdentityId' THEN 'Id'
                WHEN 'UserName' THEN 'User Name'
                WHEN 'DisplayName' THEN 'Display Name'
                WHEN 'GivenName' THEN 'Given Name'
                WHEN 'FamilyName' THEN 'Family Name'
                WHEN 'MiddleName' THEN 'Middle Name'
                WHEN 'CostCenter' THEN 'Cost Center'
                WHEN 'ManagerId' THEN 'Manager'
                WHEN 'S1OrganizationalUnitId' THEN 'Organizational Unit'
                ELSE REPLACE(REPLACE(c.[name], 'S1Identity', ''), 'S1', '')
            END AS [DisplayName],
            CASE
                WHEN c.[name] = 'Email' THEN '^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$'
                WHEN TYPE_NAME(c.[system_type_id]) = 'uniqueidentifier' THEN '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$'
                ELSE '.*'
            END AS [RegularExpression],
            CASE
                WHEN c.[name] IN ('S1IdentityId', 'UserName', 'DisplayName', 'Email', 'GivenName', 'FamilyName', 'Department', 'Division', 'Active') THEN 1
                ELSE 0
            END AS [FilterBy]
        FROM sys.columns c
        WHERE c.[object_id] = OBJECT_ID('S1Identity')
        ORDER BY c.[column_id];
        RETURN;
    END

    -- Mode 3: Count
    IF @metadata = 3
    BEGIN
        SELECT COUNT(*) AS [Count]
        FROM [dbo].[S1Identity]
        WHERE [S1IdentityDeleted] IS NULL;
        RETURN;
    END

    -- Mode 1: Minimal
    IF @metadata = 1
    BEGIN
        SELECT [S1IdentityId] AS [Id], [DisplayName]
        FROM [dbo].[S1Identity]
        WHERE [S1IdentityDeleted] IS NULL
          AND (@S1IdentityId IS NULL OR [S1IdentityId] = @S1IdentityId);
        RETURN;
    END

    -- Mode 2: Properties
    IF @metadata = 2
    BEGIN
        SELECT
            c.[name] AS [ColumnName],
            TYPE_NAME(c.[system_type_id]) AS [DataType],
            c.[max_length] AS [MaxLength],
            c.[is_nullable] AS [IsNullable]
        FROM sys.columns c
        WHERE c.[object_id] = OBJECT_ID('S1Identity')
        ORDER BY c.[column_id];
        RETURN;
    END

    -- Mode 0: Full data
    DECLARE @sql NVARCHAR(MAX);

    SET @sql = N'SELECT
        i.[S1IdentityId],
        i.[S1IdentityIdentity],
        i.[Name],
        i.[UserName],
        i.[DisplayName],
        i.[Email],
        i.[GivenName],
        i.[FamilyName],
        i.[MiddleName],
        i.[Title],
        i.[Department],
        i.[Division],
        i.[CostCenter],
        i.[ManagerId],
        m.[DisplayName] AS [ManagerDisplayName],
        i.[S1OrganizationalUnitId],
        ou.[DisplayName] AS [OrganizationalUnitDisplayName],
        i.[Active],
        i.[S1IdentityInserted],
        i.[S1IdentityInsertedBy],
        i.[S1IdentityUpdated],
        i.[S1IdentityUpdatedBy]
    FROM [dbo].[S1Identity] i
    LEFT JOIN [dbo].[S1Identity] m ON i.[ManagerId] = m.[S1IdentityId]
    LEFT JOIN [dbo].[S1OrganizationalUnit] ou ON i.[S1OrganizationalUnitId] = ou.[S1OrganizationalUnitId]
    WHERE i.[S1IdentityDeleted] IS NULL';

    IF @S1IdentityId IS NOT NULL
        SET @sql = @sql + N' AND i.[S1IdentityId] = @S1IdentityId';

    IF @search IS NOT NULL
        SET @sql = @sql + N' AND (i.[DisplayName] LIKE ''%'' + @search + ''%'' OR i.[UserName] LIKE ''%'' + @search + ''%'' OR i.[Email] LIKE ''%'' + @search + ''%'')';

    IF @orderby IS NOT NULL
        SET @sql = @sql + N' ORDER BY ' + @orderby;
    ELSE
        SET @sql = @sql + N' ORDER BY i.[DisplayName]';

    SET @sql = @sql + N' OFFSET @skip ROWS FETCH NEXT @top ROWS ONLY';

    EXEC sp_executesql @sql,
        N'@S1IdentityId UNIQUEIDENTIFIER, @search NVARCHAR(256), @top INT, @skip INT',
        @S1IdentityId, @search, @top, @skip;
END
GO
