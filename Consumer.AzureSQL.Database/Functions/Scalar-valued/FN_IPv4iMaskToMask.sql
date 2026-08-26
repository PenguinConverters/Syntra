CREATE FUNCTION [dbo].[FN_IPv4iMaskToMask]
(
	@mask TINYINT
)
RETURNS VARCHAR(15)
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.08.14
-- Project:				Syntra
-- Description:			Converts decimal mask to octet mask
-- ========================================================
BEGIN
	DECLARE @Result VARCHAR(15);

	WITH Octets
	As
	(
	SELECT
		@mask / 8 Octets,
		@mask % 8 RestBytes
	),
	IpMask
	As
	(
	SELECT
		CASE
			WHEN OCT.Octets > 0 THEN 255
			WHEN OCT.Octets = 0 THEN (POWER(2, OCT.[RestBytes])-1) << (8-[RestBytes])
			ELSE 0
		END As [0],
		CASE
			WHEN OCT.Octets > 1 THEN 255
			WHEN OCT.Octets = 1 THEN (POWER(2, OCT.[RestBytes])-1) << (8-[RestBytes])
			ELSE 0
		END [1],
		CASE
			WHEN OCT.Octets > 2 THEN 255
			WHEN OCT.Octets = 2 THEN (POWER(2, OCT.[RestBytes])-1) << (8-[RestBytes])
			ELSE 0
		END [2],
		CASE
			WHEN OCT.Octets > 3 THEN 255
			WHEN OCT.Octets = 3 THEN (POWER(2, OCT.[RestBytes])-1) << (8-[RestBytes])
			ELSE 0
		END [3]
	FROM
		Octets OCT
	),
	Mask As
	(
		SELECT
			CONCAT_WS('.', [0], [1], [2], [3]) As [Value]
		FROM
			IpMask
	)
	SELECT @Result = [Value] FROM Mask
	RETURN @Result
END
