CREATE FUNCTION [dbo].[FN_IPv4iMaskToInteger]
(
	@mask TINYINT
)
RETURNS BIGINT
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.08.14
-- Project:				Syntra
-- Description:			Converts decimal mask to integer mask
-- ========================================================
BEGIN
	DECLARE
		@Octets TINYINT = @mask / 8,
		@RestBytes TINYINT = @mask % 8;

	RETURN
	(
		(
			(
				((CAST(256 As BIGINT)*(256))*(256))
				*
				(
				CASE
					WHEN @Octets > 0 THEN 255
					WHEN @Octets = 0 THEN (POWER(2, @RestBytes)-1) << (8-@RestBytes)
					ELSE 0
				END
				)
				+
				((256)*(256))
				*
				(
				CASE
					WHEN @Octets > 1 THEN 255
					WHEN @Octets = 1 THEN (POWER(2, @RestBytes)-1) << (8-@RestBytes)
					ELSE 0
				END
				)
			)
		+
			(256)
			*
			(
			CASE
				WHEN @Octets > 2 THEN 255
				WHEN @Octets = 2 THEN (POWER(2, @RestBytes)-1) << (8-@RestBytes)
				ELSE 0
			END
			)
		)
		+
		(
		CASE
			WHEN @Octets > 3 THEN 255
			WHEN @Octets = 3 THEN (POWER(2, @RestBytes)-1) << (8-@RestBytes)
			ELSE 0
		END
		)
	)
END
