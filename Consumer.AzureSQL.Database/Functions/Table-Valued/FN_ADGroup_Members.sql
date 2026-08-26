CREATE FUNCTION [dbo].[FN_ADGroup_Members]
(
	@objectGUID VARCHAR(36) = '%',
	@objectSID VARCHAR(100) = '%',
	@sAMAccountName VARCHAR(128) = '%',
	@distinguishedName VARCHAR(1700) = '%',
	@NTLogonName VARCHAR(2048) = '%'
)

RETURNS TABLE

As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.09.24
-- Project:				Syntra
-- Description:			Returns ADGroup effective members
-- ========================================================
RETURN

WITH EMembers
As
(
	SELECT
		[groupDistinguishedName] As [parentDistinguishedName],
		[groupDistinguishedName],
		GRP.[ADGroupId] As [groupId],
		GRP.[objectGUID] As [groupObjectGUID],
		GRP.[objectSID] As [groupObjectSID],
		GRP.[sAMAccountName] As [groupSAMAccountName],
		GRP.[msDS-PrincipalName] As [groupNTLogonName],
		GRP.[objectClass] As [groupObjectClass],
		GRP.[ADGroupDomainFQDN] As [groupDomainFQDN],
		REL.[memberDistinguishedName],
		MBR.[PrincipalId] As [memberId],
		MBR.[objectGUID] As [memberObjectGUID],
		MBR.[objectSID] As [memberObjectSID],
		MBR.[sAMAccountName] As [memberSAMAccountName],
		MBR.[msDS-PrincipalName] As [memberNTLogonName],
		MBR.[objectClass] As [memberObjectClass],
		MBR.[DomainFQDN] As [memberDomainFQDN],
		CAST([ADMemberId] As VARCHAR(1700)) As [Path],
		0 As [Index]
	FROM
		VA_ADMember REL
	JOIN
		VX_ADSecurityPrincipal MBR
	ON
		REL.[memberDistinguishedName] = MBR.[distinguishedName]
	JOIN
		VA_ADGroup GRP
	ON
		REL.[groupDistinguishedName] = GRP.[distinguishedName]
	WHERE
			GRP.[objectGUID] LIKE @objectGUID
		AND
			GRP.[objectSID] LIKE @objectSID
		AND
			GRP.[sAMAccountName] LIKE @sAMAccountName
		AND
			GRP.[distinguishedName] LIKE @distinguishedName
		AND
			GRP.[msDS-PrincipalName] LIKE @NTLogonName
	UNION ALL
	SELECT
		PAR.[parentDistinguishedName] As [parentDistinguishedName],
		PAR.[memberDistinguishedName],
		PAR.[memberId] As [groupId],
		PAR.[memberObjectGUID] As [groupObjectGUID],
		PAR.[memberObjectSID] As [groupObjectSID],
		PAR.[memberSAMAccountName] As [groupSAMAccountName],
		PAR.[memberNTLogonName] As [groupNTLogonName],
		PAR.[memberObjectClass] As [groupObjectClass],
		PAR.[memberDomainFQDN] As [groupDomainFQDN],
		REL.[memberDistinguishedName],
		MBR.[PrincipalId] As [memberId],
		MBR.[objectGUID] As [memberObjectSID],
		MBR.[objectSID] As [memberObjectSID],
		MBR.[sAMAccountName] As [memberSAMAccountName],
		MBR.[msDS-PrincipalName] As [memberNTLogonName],
		MBR.[objectClass] As [memberObjectClass],
		MBR.[DomainFQDN] As [memberDomainFQDN],
		CAST(PAR.[Path] + REL.[ADMemberId] As VARCHAR(1700)),
		PAR.[Index] + 1
	FROM
		EMembers PAR
	JOIN
		VA_ADMember REL
	ON
		PAR.[memberDistinguishedName] = REL.[groupDistinguishedName]
	JOIN
		VX_ADSecurityPrincipal MBR
	ON
		REL.[memberDistinguishedName] = MBR.[distinguishedName]
	WHERE
		PAR.[Path] NOT LIKE '%' + REL.[ADMemberId] + '%'
)

SELECT
	*
FROM
	EMembers EMB