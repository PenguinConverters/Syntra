CREATE VIEW [dbo].[VA_ADForeignSecurityPrincipals]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Active rows of [VW_ADForeignSecurityPrincipals],
--						excluding soft-deleted records
-- ========================================================
	SELECT
		*
	FROM
		[dbo].[VW_ADForeignSecurityPrincipals]
	WHERE
		[ADForeignSecurityPrincipalsDeleted] Is NULL