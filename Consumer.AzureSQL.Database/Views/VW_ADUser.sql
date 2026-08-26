CREATE VIEW [dbo].[VW_ADUser]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Base view over [ADUser] adding
--						decoded disabled flag and a RowVersion alias
-- ========================================================
	SELECT
		*,
		CAST(userAccountControl & 0x0002 As BIT) As [userAccountControl_disabled],
		[ADUserRowVersion] As [RowVersion]
	FROM
		[ADUser]