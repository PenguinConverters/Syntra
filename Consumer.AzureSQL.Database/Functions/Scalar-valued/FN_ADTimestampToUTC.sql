CREATE FUNCTION [dbo].[FN_ADTimestampToUTC]
(
	@timestamp BIGINT
)
RETURNS DATETIME2
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.08.14
-- Project:				Syntra
-- Description:			Converts AD timestamp to UTC DateTime
-- ========================================================
BEGIN
	RETURN
		CASE
			WHEN @timestamp > 0x0000000000000000 AND @timestamp < 0x7FFFFFFFFFFFFFFF THEN CAST((@timestamp / 864000000000.0 - 109207) AS DATETIME)
			WHEN ISNULL(@timestamp, 0x0000000000000000) = 0x0000000000000000 THEN CAST(0x0000000000000000 As DATETIME)
		END
END
