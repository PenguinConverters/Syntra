CREATE VIEW [dbo].[VA_ADGroupManagedServiceAccount]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Active rows of [VW_ADGroupManagedServiceAccount],
--						excluding soft-deleted records
-- ========================================================
	SELECT
		*
	FROM
		[dbo].[VW_ADGroupManagedServiceAccount]
	WHERE
		[ADGroupManagedServiceAccountDeleted] Is NULL