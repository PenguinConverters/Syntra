CREATE FUNCTION [dbo].[FN_COUNTINSTR]
(
	@expressionToFind VARCHAR(MAX),	--A character expression containing the sequence to find
	@expressionToSearch VARCHAR(MAX) --A character expression to search
)
RETURNS INT
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.05.28
-- Project:				Syntra
-- Description:			Returns string occurrences count
--						within another string.
-- ========================================================
BEGIN
	DECLARE @Result INT = 0;

	WITH POSINSTR As
	(
		SELECT
			CHARINDEX(@expressionToFind, @expressionToSearch, 0) As [AtIndex]
		UNION ALL
		SELECT
			CHARINDEX(@expressionToFind, @expressionToSearch, CNT.[AtIndex]+1) As [AtIndex]
		FROM
			POSINSTR  CNT
		WHERE
			CNT.[AtIndex] > 0
	),
	FN_COUNTINSTR As
	(
		SELECT
			COUNT(*) - 1 As [CharCount]
		FROM
			POSINSTR
	)

	
	SELECT @Result = [CharCount] FROM FN_COUNTINSTR

	RETURN @Result
END
