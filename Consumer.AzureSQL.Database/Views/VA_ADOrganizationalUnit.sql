CREATE VIEW [dbo].[VA_ADOrganizationalUnit]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Active rows of [VW_ADOrganizationalUnit],
--						excluding soft-deleted records
-- ========================================================
	SELECT
		*
	FROM
		[dbo].[VW_ADOrganizationalUnit]
	WHERE
		[ADOrganizationalUnitDeleted] Is NULL