CREATE FUNCTION [dbo].[FN_IPv4NetworkAddress]
(
	@network VARCHAR(18)
)
RETURNS VARCHAR(15)
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.08.14
-- Project:				Syntra
-- Description:			Extracts IPv4 Network Address
--						e.g. 10.250.174.128/26 -> 10.250.174.128
-- ========================================================
BEGIN
	RETURN SUBSTRING(@network, 0, CHARINDEX('/', @network))
END
