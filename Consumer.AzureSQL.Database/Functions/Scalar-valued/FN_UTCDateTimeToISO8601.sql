CREATE FUNCTION [dbo].[FN_UTCDateTimeToISO8601]
(
	@dateTime DATETIME2
)
RETURNS VARCHAR(28)
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.09.24
-- Project:				Syntra
-- Description:			Converts DateTime to UTC ISO8601 DateTime
-- ========================================================
BEGIN
	RETURN FORMAT(@dateTime, 'yyyy-MM-ddTHH:mm:ss.fffffffZ')
END
