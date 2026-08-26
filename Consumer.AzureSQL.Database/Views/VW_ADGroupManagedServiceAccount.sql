CREATE VIEW [dbo].[VW_ADGroupManagedServiceAccount]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Base view over [ADGroupManagedServiceAccount] adding
--						a RowVersion alias
-- ========================================================
	SELECT
		*,
		[ADGroupManagedServiceAccountRowVersion] As [RowVersion]
	FROM
		[ADGroupManagedServiceAccount]