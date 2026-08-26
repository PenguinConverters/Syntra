CREATE FUNCTION [dbo].[FN_CEILING]
(
	@value int,
	@max int
)
RETURNS INT
AS
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Returns the lower of value and maximum
-- ========================================================
BEGIN
	RETURN CASE WHEN @value > @max THEN @max ELSE @value END
END
