CREATE VIEW [dbo].[VW_ADMember]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Base view over [ADMember] adding
--						a RowVersion alias
-- ========================================================
	SELECT
		*,
		[ADMemberRowVersion] As [RowVersion]
	FROM
		[ADMember]