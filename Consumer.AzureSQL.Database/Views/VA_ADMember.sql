CREATE VIEW [dbo].[VA_ADMember]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Active rows of [VW_ADMember],
--						excluding soft-deleted records
-- ========================================================
	SELECT
		*
	FROM
		[dbo].[VW_ADMember]
	WHERE
		[ADMemberDeleted] Is NULL