CREATE VIEW [dbo].[VX_ADMember]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.05.29
-- Project:				Syntra
-- Description:			Returns all AD Memberships
--						Including resolved Security Principals
-- ========================================================
	SELECT
		MBR.*,
		GRP.ADGroupId As [groupId],
		GRP.objectSID As groupObjectSID,
		GRP.objectGUID As groupObjectGUID,
		GRP.cn As groupCn,
		GRP.displayName As groupDisplayName,
		GRP.[msDS-PrincipalName] As groupNTLogonName,
		MEM.[PrincipalId] As memberId,
		MEM.[objectSID] As memberObjectSID,
		MEM.[objectGUID] As memberObjectGUID,
		MEM.[cn] As memberCn,
		MEM.[displayName] As memberDisplayName,
		MEM.[msDS-PrincipalName] As memberNTLogonName,
		MEM.[objectClass],
		MEM.[DomainFQDN] As [memberDomainFQDN]
	FROM
		[VA_ADMember] MBR
	JOIN
		[VA_ADGroup] GRP
	ON
		MBR.[groupDistinguishedName] = GRP.[distinguishedName]
	JOIN
		[VX_ADSecurityPrincipal] MEM
	ON
		MBR.[memberDistinguishedName] = MEM.[distinguishedName]