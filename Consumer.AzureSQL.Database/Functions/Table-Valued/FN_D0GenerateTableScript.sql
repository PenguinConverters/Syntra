CREATE FUNCTION [dbo].[FN_D0GenerateTableScript]
(
	@TableName VARCHAR(128)
)
RETURNS @returntable TABLE
(
	[TableScript] VARCHAR(MAX),
	[TriggerScript] VARCHAR(MAX),
	[ViewScript] VARCHAR(MAX),
	[ActiveViewScript] VARCHAR(MAX)
)
AS
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Generates table, trigger and view scripts
--						for a synchronised entity table
-- ========================================================
BEGIN
	INSERT @returntable
	SELECT
CONCAT(
'CREATE TABLE [dbo].', QUOTENAME(@TableName), '
(
	', QUOTENAME(@TableName + 'Id'), ' VARCHAR(36) NOT NULL PRIMARY KEY DEFAULT LOWER(NEWSEQUENTIALID()),
	', QUOTENAME(@TableName + 'Identity'), ' INT IDENTITY(0, 1) NOT NULL,
	', QUOTENAME(@TableName + 'Updated'), ' DATETIME2 NULL DEFAULT GETUTCDATE(),
	', QUOTENAME(@TableName + 'UpdatedBy'), ' VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	', QUOTENAME(@TableName + 'Inserted'), ' DATETIME2 NULL DEFAULT GETUTCDATE(),
	', QUOTENAME(@TableName + 'InsertedBy'), ' VARCHAR(200) NULL DEFAULT SUSER_SNAME(),
	', QUOTENAME(@TableName + 'Deleted'), ' DATETIME2 NULL,
	', QUOTENAME(@TableName + 'RowVersion'), ' ROWVERSION NOT NULL
)

GO') As [TableScript],
 
CONCAT(
'CREATE TRIGGER [dbo].', QUOTENAME('Trigger_' + @TableName + '_' + @TableName + 'Updated-UPDATE'),
'	ON [dbo].', QUOTENAME(@TableName),'	FOR UPDATE
As
	BEGIN
		UPDATE
			',	QUOTENAME(@TableName), '
		SET
			', QUOTENAME(@TableName + 'Updated'), ' = GETUTCDATE(),
			', QUOTENAME(@TableName + 'UpdatedBy'), ' = SUSER_SNAME()
		WHERE
			', QUOTENAME(@TableName + 'Id'), ' In
			(
				SELECT DISTINCT
						', QUOTENAME(@TableName + 'Id'),'
				FROM
						Inserted
			)
	END
GO') As [TriggerScript],

CONCAT(
'CREATE VIEW [dbo].', QUOTENAME('VW_' + @TableName), '
As
	SELECT
		*,
		', QUOTENAME(@TableName + 'RowVersion'), ' As [RowVersion]
	FROM
		', QUOTENAME(@TableName)
) As [ViewScript],
 
 CONCAT(
'CREATE VIEW [dbo].', QUOTENAME('VA_' + @TableName), '
As
	SELECT
		*
	FROM
		[dbo].', QUOTENAME('VW_' + @TableName), '
	WHERE
		', QUOTENAME(@TableName + 'Deleted'), ' Is NULL'
) As [ActiveViewScript]
	RETURN
END
