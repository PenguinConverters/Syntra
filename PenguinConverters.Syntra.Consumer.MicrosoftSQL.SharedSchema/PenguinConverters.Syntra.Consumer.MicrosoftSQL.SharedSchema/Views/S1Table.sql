-- =============================================
-- Syntra - Penguin Converters AG
-- View: S1Table
-- Description: Generates S1TableId from TABLE_NAME hash
-- =============================================
CREATE VIEW [dbo].[S1Table]
AS
SELECT CONVERT(UNIQUEIDENTIFIER, HASHBYTES('MD5', TABLE_NAME), 0) AS [S1TableId],
       TABLE_NAME AS [Name]
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
GO
