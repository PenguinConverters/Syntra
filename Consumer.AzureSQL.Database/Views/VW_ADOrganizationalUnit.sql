CREATE VIEW [dbo].[VW_ADOrganizationalUnit]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Base view over [ADOrganizationalUnit] adding
--						a RowVersion alias
-- ========================================================
	SELECT
		*,
		[ADOrganizationalUnitRowVersion] As [RowVersion]
	FROM
		[ADOrganizationalUnit]