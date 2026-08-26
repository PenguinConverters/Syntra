CREATE FUNCTION [dbo].[FN_IsInIPv4Subnet]
(
	@subnet VARCHAR(15),
	@mask VARCHAR(15),
	@ip VARCHAR(15)
)
RETURNS BIT
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.08.14
-- Project:				Syntra
-- Description:			Verifies if IPv4 address belongs
--						to given IPv4 subnet
-- ========================================================
BEGIN
	RETURN
	IIF(
		((CAST(PARSENAME(@mask, 4) As TINYINT) & CAST(PARSENAME(@subnet, 4) As TINYINT)) ^ (CAST(PARSENAME(@mask, 4) As TINYINT) & CAST(PARSENAME(@ip, 4) As TINYINT)))
		= 0, IIF(
		((CAST(PARSENAME(@mask, 3) As TINYINT) & CAST(PARSENAME(@subnet, 3) As TINYINT)) ^ (CAST(PARSENAME(@mask, 3) As TINYINT) & CAST(PARSENAME(@ip, 3) As TINYINT)))
		= 0, IIF(
		((CAST(PARSENAME(@mask, 2) As TINYINT) & CAST(PARSENAME(@subnet, 2) As TINYINT)) ^ (CAST(PARSENAME(@mask, 2) As TINYINT) & CAST(PARSENAME(@ip, 2) As TINYINT)))
		= 0, IIF(
		((CAST(PARSENAME(@mask, 1) As TINYINT) & CAST(PARSENAME(@subnet, 1) As TINYINT)) ^ (CAST(PARSENAME(@mask, 1) As TINYINT) & CAST(PARSENAME(@ip, 1) As TINYINT)))
		= 0, 1, 0), 0), 0), 0)
END
