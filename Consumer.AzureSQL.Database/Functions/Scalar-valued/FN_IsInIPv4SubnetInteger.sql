CREATE FUNCTION [dbo].[FN_IsInIPv4SubnetInteger]
(
	@subnet BIGINT,
	@mask BIGINT,
	@ip BIGINT
)
RETURNS BIT
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.12.03
-- Project:				Syntra
-- Description:			Verifies if IPv4 address belongs
--						to given IPv4 subnet
-- ========================================================
BEGIN
	RETURN
	IIF(
	(
		(@ip & @mask)
		^
		(@subnet & @mask)
	) = 0, 1, 0)
END
