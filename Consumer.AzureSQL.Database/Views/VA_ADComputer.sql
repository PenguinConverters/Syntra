CREATE VIEW [dbo].[VA_ADComputer]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Active rows of [VW_ADComputer],
--						excluding soft-deleted records
-- ========================================================
	SELECT
		*
	FROM
		[dbo].[VW_ADComputer]
	WHERE
		[ADComputerDeleted] Is NULL