CREATE FUNCTION [dbo].[FN_IPv4NetworkMask]
(
	@network VARCHAR(18)
)
RETURNS VARCHAR(15)
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.08.14
-- Project:				Syntra
-- Description:			Extracts IPv4 Network Mask
--						e.g. 10.250.174.128/26 -> 255.255.255.192
-- ========================================================
BEGIN
	RETURN [dbo].[FN_IPv4iMaskToMask](CAST(SUBSTRING(@network, CHARINDEX('/', @network)+1, 2) As TINYINT))
END
