CREATE FUNCTION [dbo].[FN_TEXTTOGUID]
(
	@text VARCHAR(300)
)
RETURNS VARCHAR(36)
AS
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2026.08.26
-- Project:				Syntra
-- Description:			Derives a deterministic GUID string from text
-- ========================================================
BEGIN
	RETURN
		STUFF(STUFF(STUFF(STUFF(
			LOWER( CONVERT(VARCHAR(32), HashBytes('MD5', @text), 2)),
			9,	0,	'-'),
			14,	0,	'-'),
			19,	0,	'-'),
			24,	0,	'-')
END
