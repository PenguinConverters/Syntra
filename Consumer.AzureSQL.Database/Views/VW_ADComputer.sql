CREATE VIEW [dbo].[VW_ADComputer]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Base view over [ADComputer] adding
--						a RowVersion alias
-- ========================================================
	SELECT
		*,
		[ADComputerRowVersion] As [RowVersion]
	FROM
		[ADComputer]