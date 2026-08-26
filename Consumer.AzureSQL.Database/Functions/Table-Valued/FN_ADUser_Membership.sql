CREATE FUNCTION [dbo].[FN_ADUser_Membership]
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
-- Created:				2024.08.13
-- Project:				Syntra
-- Description:			Returns ADUser effective memberships
-- ========================================================
RETURN
WITH Members
As
	(
		SELECT
			[objectGUID],
			[distinguishedName]
		FROM
			[VA_ADUser]
		UNION ALL
		SELECT
			[objectGUID],
			[distinguishedName]
		FROM
			[VA_ADGroup]
	)
,
Membership
As
	(
		SELECT
			MBR.[ADMemberId],
			MBR.[ADMemberDomainFQDN],
			[memberDistinguishedName],
			CHL.[objectGUID] As [memberObjectGUID],		
			[groupDistinguishedName],
			GRP.[objectGUID] As [groupObjectGUID]
		FROM
			[VA_ADMember] MBR
		JOIN
			[VA_ADGroup] GRP
		ON
			GRP.[distinguishedName] = MBR.[groupDistinguishedName]
		JOIN
			[Members] CHL
		ON
			CHL.[distinguishedName] = MBR.[memberDistinguishedName]
	)	
,
EMembership
As
(
	SELECT
		[memberDistinguishedName] As [childDistinguishedName],
		[memberDistinguishedName],
		USR.[ADUserId] As [memberId],
		USR.[objectGUID] As [memberObjectGUID],
		USR.[objectSID] As [memberObjectSID],
		USR.[sAMAccountName] As [memberSAMAccountName],
		USR.[msDS-PrincipalName] As [memberNTLogonName],
		USR.[objectClass] As [memberObjectClass],
		USR.[ADUserDomainFQDN] As [memberDomainFQDN],
		[groupDistinguishedName],
		GRP.[ADGroupId] As [groupId],
		GRP.[objectGUID] As [groupObjectGUID],
		GRP.[objectSID] As [groupObjectSID],
		GRP.[sAMAccountName] As [groupSAMAccountName],
		GRP.[msDS-PrincipalName] As [groupNTLogonName],
		GRP.[objectClass] As [groupObjectClass],
		GRP.[ADGroupDomainFQDN] As [groupDomainFQDN],
		CAST([ADMemberId] As VARCHAR(1700)) As [Path],
		0 As [Index]
	FROM
		Membership MBR WITH (NOLOCK)
	JOIN
		VA_ADUser USR WITH (NOLOCK)
	ON
		MBR.[memberObjectGUID] = USR.[objectGUID]
	AND
		ISNULL(MBR.[ADMemberDomainFQDN], '') = ISNULL(USR.[ADUserDomainFQDN], '')
	JOIN
		VA_ADGroup GRP WITH (NOLOCK)
	ON
		MBR.[groupObjectGUID] = GRP.[objectGUID]
	AND
		ISNULL(MBR.[ADMemberDomainFQDN], '') = ISNULL(GRP.[ADGroupDomainFQDN], '')
	WHERE
			USR.[objectGUID] LIKE @objectGUID
		AND
			USR.[objectSID] LIKE @objectSID
		AND
			USR.[sAMAccountName] LIKE @sAMAccountName
		AND
			USR.[distinguishedName] LIKE @distinguishedName
		AND
			USR.[msDS-PrincipalName] LIKE @NTLogonName
	UNION ALL
	SELECT
		PAR.[memberDistinguishedName] As [childDistinguishedName],
		MBR.[memberDistinguishedName],
		PAR.[groupId] As [memberId],
		PAR.[groupObjectGUID] As [memberObjectGUID],
		PAR.[groupObjectSID] As [memberObjectSID],
		PAR.[groupSAMAccountName] As [memberSAMAccountName],
		PAR.[groupNTLogonName] As [memberNTLogonName],
		PAR.[groupObjectClass] As [memberObjectClass],
		PAR.[groupDomainFQDN] As [memberDomainFQDN],
		MBR.[groupDistinguishedName],
		GRP.[ADGroupId] As [groupId],
		GRP.[objectGUID] As [groupObjectGUID],
		GRP.[objectSID] As [groupObjectSID],
		GRP.[sAMAccountName] As [groupSAMAccountName],
		GRP.[msDS-PrincipalName] As [groupNTLogonName],
		GRP.[objectClass] As [groupObjectClass],
		GRP.[ADGroupDomainFQDN] As [groupDomainFQDN],
		CAST(PAR.[Path] + MBR.[ADMemberId] As VARCHAR(1700)),
		PAR.[Index] + 1
	FROM
		[Membership] MBR WITH (NOLOCK)
	JOIN
		EMembership PAR
	ON
		MBR.[memberObjectGUID] = PAR.[groupObjectGUID]
	JOIN
		VA_ADGroup GRP WITH (NOLOCK)
	ON
		MBR.[groupObjectGUID] = GRP.[objectGUID]
	WHERE
		PAR.[Path] NOT LIKE '%' + MBR.[ADMemberId] + '%'
)

SELECT
	*
FROM
	EMembership EMB