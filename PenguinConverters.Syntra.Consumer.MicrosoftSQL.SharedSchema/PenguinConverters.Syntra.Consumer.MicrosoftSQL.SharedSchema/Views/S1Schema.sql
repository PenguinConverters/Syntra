-- =============================================
-- Syntra - Penguin Converters AG
-- View: S1Schema
-- Description: Generates S1SchemaId from TABLE_NAME.COLUMN_NAME hash
-- =============================================
CREATE VIEW [dbo].[S1Schema]
AS
SELECT CONVERT(UNIQUEIDENTIFIER, HASHBYTES('MD5', c.TABLE_NAME + '.' + c.COLUMN_NAME), 0) AS [S1SchemaId],
       CONVERT(UNIQUEIDENTIFIER, HASHBYTES('MD5', c.TABLE_NAME), 0) AS [S1TableId],
       c.TABLE_NAME AS [TableName],
       c.COLUMN_NAME AS [ColumnName],
       c.DATA_TYPE AS [DataType],
       c.CHARACTER_MAXIMUM_LENGTH AS [MaxLength],
       CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS [IsNullable],
       c.ORDINAL_POSITION AS [OrdinalPosition]
FROM INFORMATION_SCHEMA.COLUMNS c
INNER JOIN INFORMATION_SCHEMA.TABLES t ON c.TABLE_NAME = t.TABLE_NAME AND t.TABLE_TYPE = 'BASE TABLE'
GO
