CREATE FUNCTION [dbo].[FN_IPv4ToInteger]
(
	@ip As VARCHAR(15)
)
RETURNS BIGINT
WITH SCHEMABINDING
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.12.03
-- Project:				Syntra
-- Description:			Converts string IPv4 to Number
-- ========================================================
BEGIN
	RETURN
	(
		(
			(
				((CAST(256 As BIGINT)*(256))*(256)) * PARSENAME(@ip, 4)
				+
				((256)*(256)) * PARSENAME(@ip, 3)
			)
		+
			(256) * PARSENAME(@ip, 2)
		) + TRY_PARSE(PARSENAME(@ip, 1) As INT)
	)
END