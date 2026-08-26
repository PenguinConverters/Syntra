
CREATE FUNCTION [dbo].[FN_ADSecurityPrincipalGuid]
(
	@distinguishedName VARCHAR(1700)
)
RETURNS VARCHAR(36)
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2025.03.26
-- Project:				Syntra
-- Description:			Converts AD distinguishedName into objectGUID
--						only for Security Principals
-- ========================================================
BEGIN
	DECLARE @objectGUID VARCHAR(36)
	SELECT @objectGUID =
		[ObjectGUID]
	FROM
		[VX_ADSecurityPrincipal]
	WHERE
		[distinguishedName] = @distinguishedName

	RETURN @objectGUID
END
