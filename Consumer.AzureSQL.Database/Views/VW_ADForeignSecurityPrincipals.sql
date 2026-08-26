CREATE VIEW [dbo].[VW_ADForeignSecurityPrincipals]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Base view over [ADForeignSecurityPrincipals] adding
--						a RowVersion alias
-- ========================================================
	SELECT
		*,
		[ADForeignSecurityPrincipalsRowVersion] As [RowVersion]
	FROM
		[ADForeignSecurityPrincipals]