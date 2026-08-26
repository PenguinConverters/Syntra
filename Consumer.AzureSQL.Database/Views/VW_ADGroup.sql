CREATE VIEW [dbo].[VW_ADGroup]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Base view over [ADGroup] adding
--						decoded Security flag and a RowVersion alias
-- ========================================================
	-- GROUP_TYPE_SECURITY_ENABLED BIGINT = 0x80000000

	SELECT
		*,
		CAST(groupType & 0x80000000 As BIT) As [Security],
		[ADGroupRowVersion] As [RowVersion]
	FROM
		[ADGroup]