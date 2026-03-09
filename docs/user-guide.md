# Syntra - User Guide

## Introduction

Syntra is an IAM (Identity & Access Management) and Automation framework by Penguin Converters AG. It synchronizes data from multiple sources to multiple destinations, provides transaction workflows with approval chains, and exposes a dynamic REST API.

## Use Cases

### Data Synchronization
- Sync Active Directory users/groups to Azure SQL
- Sync Entra ID (Azure AD) service principals and roles
- Sync ServiceNow CMDB records
- Sync Azure Resource Manager subscriptions, policies, role assignments

### Data Auditing
- Track data transformations with shadow copies
- Use PowerBI or other reporting tools on synced data
- Identify security gaps on near-live data

### Security Analysis
- Model security relationships as directed graph (S1Node/S1Edge)
- Calculate Confidentiality and Availability factors
- Identify cross-domain lateral movement paths

### Identity Governance
- Transaction-based change management with approval workflows
- Role-based access with dynamic approver selection
- Audit trail for all operations

## Running Syntra

### Console Mode (One-Time Sync)

```bash
# Full sync
dotnet PenguinConverters.Syntra.Host.Console.dll --configuration=config.yaml

# Schema discovery mode (output SQL schema without writing)
dotnet PenguinConverters.Syntra.Host.Console.dll --configuration=config.yaml --schema
```

### Windows Service

```bash
# Install as Windows Service
sc create Syntra binpath="C:\Syntra\PenguinConverters.Syntra.Host.Service.exe"
sc start Syntra
```

Place configuration files in a `Configuration/` subdirectory. Each `.yaml` or `.json` file defines a sync job with a cron schedule.

### Linux Daemon (systemd)

```ini
[Unit]
Description=Syntra Synchronization Service

[Service]
Type=notify
ExecStart=/opt/syntra/PenguinConverters.Syntra.Host.Service

[Install]
WantedBy=multi-user.target
```

## Configuration

### YAML Configuration File

```yaml
ObjectNamespace: "ActiveDirectory.Users"
Delta: true
MaxDegreeOfParallelism: 3
SchemaDesigner: false

Source:
  Type: "PenguinConverters.Syntra.Provider.ActiveDirectory"
  BaseDN: "DC=corp,DC=example,DC=com"
  ServerName: "dc01.corp.example.com"
  Port: 636
  SecureSocketLayer: true
  LdapFilter: "(objectClass=user)"
  Username:
    Value: "svc-syntra@corp.example.com"
    Protected: false
  Password:
    Value: "BASE64_ENCRYPTED_VALUE"
    Protected: true
  Delta: true

Target:
  Type: "PenguinConverters.Syntra.Consumer.AzureSQL"
  TableName: "ADUser"
  ConnectionString:
    Value: "BASE64_ENCRYPTED_VALUE"
    Protected: true
  PrimaryKeys:
    objectGUID: "objectGUID"
  Columns:
    - objectGUID
    - sAMAccountName
    - displayName
    - mail
    - department
    - manager
  Threshold: 10
  MaxDegreeOfParallelism: 3

Trigger:
  Crontab: "0 */4 * * *"
```

### Credential Protection

Credentials are encrypted using the Keyra SDK:

```bash
# Encrypt a value using Keyra CLI
CMDKEYRA encrypt --keyfile vault.keyra --value "MyPassword123"
# Output: BASE64_ENCRYPTED_VALUE
```

Set `Protected: true` and use the encrypted value in configuration.

## REST API

### Authentication
- **Windows Integrated (Negotiate)**: Kerberos/NTLM for on-premises
- **JWT Bearer (OBO Flow)**: Azure AD tokens for cloud deployments

### Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| GET | `api/{entity}` | Query entity data with OData parameters |
| POST | `api/{entity}` | Create new record (maps to SP_S1FE_{entity}_CREATE) |
| PATCH | `api/{entity}` | Update record (maps to SP_S1FE_{entity}_UPDATE) |
| DELETE | `api/{entity}` | Delete record (maps to SP_S1FE_{entity}_DELETE) |
| GET | `api/$metadata` | EDM/CSDL schema (OData $metadata) |
| GET | `api/openapi.json` | Dynamic OpenAPI 3.0 specification |

### OData Query Parameters

```
GET api/ADUser?$filter=department eq 'Engineering'&$orderby=displayName&$top=50&$skip=0
GET api/ADUser?$select=displayName,mail,department&$filter=contains(mail,'@example.com')
GET api/ADUser?@syntra.metadata=properties
GET api/ADUser?@syntra.metadata=count
```

| Parameter | Description |
|-----------|-------------|
| `$filter` | Filter expression (eq, ne, gt, ge, lt, le, contains, startswith, endswith) |
| `$orderby` | Sort columns (asc/desc) |
| `$top` | Maximum results (default 100, max 1500) |
| `$skip` | Skip N results |
| `$skiptoken` | Pagination token |
| `$select` | Column projection |
| `$apply` | Aggregation (distinct) |
| `@syntra.metadata` | Metadata level: parameters, properties, minimal, count |

## Schema Designer

The Schema Designer analyzes source data to infer SQL table schemas:

```bash
dotnet PenguinConverters.Syntra.Host.Console.dll --configuration=config.yaml --schema
```

Output includes:
- JSON field definitions with detected data types
- T-SQL `CREATE TABLE` statement with appropriate column types
- MaxLength calculations for string columns

## Transaction Workflows

### Creating Transactions
Transactions are created via the API or directly in SQL:

```sql
EXEC SP_S1Transaction_CREATE
    @S1TransactionOperationName = 'CreateUser',
    @ObjectId01 = 'user-guid-here',
    @PropertyJson01 = '{"displayName": "John Doe", "department": "Engineering"}'
```

### Approval Chains
Approvals are configured with dynamic approver selection:

```sql
-- S1Approval table defines the workflow
-- ApproverSelectionSql contains SQL to dynamically select approvers
-- ApprovalsRequiredCount sets how many approvals are needed
```

Logic Apps or other automation tools poll S1Transaction for new records and execute operations.

## Supported Connectors

### Providers (Sources)
| Connector | System | Delta Support |
|-----------|--------|---------------|
| ActiveDirectory | On-premises AD (LDAP) | USN-based |
| EntraID | Microsoft Entra ID / Azure AD | Graph delta tokens |
| AzureResources | Azure Resource Manager | Full sync |
| AzureSQL | SQL Server / Azure SQL | DateTime offset |
| ServiceNow | ServiceNow ITSM | DateTime offset |
| CMDB | Generic CMDB systems | DateTime offset |
| Exchange | Exchange Online | Graph API |
| DevOps | Azure DevOps | REST API |
| Oracle | Oracle Database | Planned |
| Tenable | Tenable Security Center | Planned |
| Tufin | Tufin Firewall Policy | Planned |
| Infoblox | Infoblox DNS/DHCP | Planned |
| Ciphersuite | Cipher audit | Planned |

### Consumers (Destinations)
| Connector | System | Features |
|-----------|--------|----------|
| AzureSQL | SQL Server / Azure SQL | MERGE, soft delete, shadow copies, thresholds |
| ActiveDirectory | On-premises AD (LDAP) | Attribute modification, object creation, reconciliation |
| SchemaDesigner | Console output | Type inference, T-SQL generation |
