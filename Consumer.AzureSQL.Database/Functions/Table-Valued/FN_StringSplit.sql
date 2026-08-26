CREATE FUNCTION [dbo].[FN_StringSplit]
(
	@Value VARCHAR(MAX),
	@Separator VARCHAR(MAX)
)
RETURNS TABLE

As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.09.23
-- Project:				Syntra
-- Description:			Returns string splitted by string delimitter
-- ========================================================
RETURN
(
	WITH
		Split As
	(
		SELECT
			CAST(0 As BIGINT) As [Index0],
			CAST(0 As BIGINT) As [Index1],
			CHARINDEX(@Separator, @Value) As [Index2],
			LEN(@Separator) As [SeparatorLength]
		UNION All
		SELECT
			[Index0] + 1,
			[Index2] + [SeparatorLength],
			CHARINDEX(@Separator, @Value, [Index2] + [SeparatorLength]),
			[SeparatorLength]
		FROM
			Split
		WHERE
			[Index2] > 0
	)

	SELECT
		SUBSTRING(@Value, [Index1], COALESCE(NULLIF([Index2], 0), LEN(@Value) + 1) - [Index1]) As [Value],
		[Index0]
	FROM
		Split
)