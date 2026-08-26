CREATE VIEW [dbo].[VA_ADGroup]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Active rows of [VW_ADGroup],
--						excluding soft-deleted records
-- ========================================================
	SELECT
		*
	FROM
		[dbo].[VW_ADGroup]
	WHERE
		[ADGroupDeleted] Is NULL