CREATE FUNCTION [dbo].[FN_IPv4NetworkToIPv4AndMask]
(
	@network VARCHAR(18)
)
RETURNS TABLE
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.08.14
-- Project:				Syntra
-- Description:			Converts CIDR network address into
--						IPv4 network address and octal IPv4 mask
-- ========================================================
RETURN SELECT
	SUBSTRING(@network, 0, CHARINDEX('/', @network)) As [Address],
	[dbo].[FN_IPv4iMaskToMask](CAST(SUBSTRING(@network, CHARINDEX('/', @network)+1, 2) As TINYINT)) As [Mask]
