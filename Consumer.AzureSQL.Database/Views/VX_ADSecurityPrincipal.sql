CREATE VIEW [dbo].[VX_ADSecurityPrincipal]
As
-- ========================================================
-- Author:				Gregor Spyra
-- Created:				2024.05.29
-- Project:				Syntra
-- Description:			Returns all AD Security Principals
-- ========================================================
	SELECT
		[ADUserId] As [PrincipalId],
		[distinguishedName],
		[ObjectGUID],
		[ObjectSID],
		[msDS-PrincipalName],
		[sAMAccountName],
		[cn],
		[displayName],
		[objectClass],
		[ADUserDomainFQDN] As [DomainFQDN],
		[description]
	FROM
		[VA_ADUser]
	UNION ALL
	SELECT
		[ADComputerId] As [PrincipalId],
		[distinguishedName],
		[ObjectGUID],
		[ObjectSID],
		[msDS-PrincipalName],
		[sAMAccountName],
		[cn],
		[displayName],
		[objectClass],
		[ADComputerDomainFQDN] As [DomainFQDN],
		COALESCE([description], [operatingSystem]) As [description]
	FROM
		[VA_ADComputer]
	UNION ALL
	SELECT
		[ADGroupManagedServiceAccountId] As [PrincipalId],
		[distinguishedName],
		[ObjectGUID],
		[ObjectSID],
		[msDS-PrincipalName],
		[sAMAccountName],
		[cn],
		[displayName],
		[objectClass],
		[ADGroupManagedServiceAccountDomainFQDN] As [DomainFQDN],
		[description] = NULL
	FROM
		[VA_ADGroupManagedServiceAccount]
	UNION ALL
	SELECT
		[ADGroupId] As [PrincipalId],
		[distinguishedName],
		[ObjectGUID],
		[ObjectSID],
		[msDS-PrincipalName],
		[sAMAccountName],
		[cn],
		[displayName],
		[objectClass],
		[ADGroupDomainFQDN] As [DomainFQDN],
		[description]
	FROM
		[VA_ADGroup]
	UNION ALL
	SELECT
		[ADForeignSecurityPrincipalsId] As [PrincipalId],
		[distinguishedName],
		[ObjectGUID],
		[ObjectSID],
		[ObjectSID] As [msDS-PrincipalName],
		[ObjectSID] As [sAMAccountName],
		[cn],
		[displayName],
		[objectClass],
		[ADForeignSecurityPrincipalsDomainFQDN] As [DomainFQDN],
		[description]
	FROM
		[VA_ADForeignSecurityPrincipals]