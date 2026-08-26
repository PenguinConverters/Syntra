CREATE FUNCTION [dbo].[FN_D0ViewTables]
(
	@ViewName VARCHAR(128)
)
RETURNS @returntable TABLE
(
	[TableName] VARCHAR(128)
)
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.10.28
-- Project:				Syntra
-- Description:			Returns root Table names referenced by View
-- ========================================================
BEGIN
	WITH RootTablesTree
	As
	(
		SELECT
			@ViewName As [TABLE_NAME]
		UNION
		SELECT
			[TABLE_NAME]
		FROM
			[INFORMATION_SCHEMA].[VIEW_TABLE_USAGE] SCH
		WHERE
			[VIEW_NAME] = @ViewName
		UNION ALL
		SELECT
			SCH.[TABLE_NAME]
		FROM
			[INFORMATION_SCHEMA].[VIEW_TABLE_USAGE] SCH
		JOIN
			[RootTablesTree] TBL
		ON
			TBL.[TABLE_NAME] = SCH.[VIEW_NAME]
	),
	RootTables
	As
	(
		SELECT DISTINCT
			[TABLE_NAME]
		FROM
			[RootTablesTree] TRE
		JOIN
			[SYS].[TABLES] TBL
		ON
			TBL.[name] = TRE.[TABLE_NAME]
		OR
			TBL.[name] = @ViewName
	)

	INSERT @returntable
	SELECT
	*
	FROM
		[RootTables]
	RETURN
END
