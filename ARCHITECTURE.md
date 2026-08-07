# Syntra - Architecture Documentation

## Overview

Syntra is an enterprise IAM (Identity & Access Management) and Automation framework developed by Penguin Converters AG. It provides a unified platform for:

- **Data Synchronization**: Reading from multiple sources (databases, APIs, directories) and writing to multiple destinations
- **Identity Governance**: Managing identities, roles, relationships, and organizational structures
- **Transaction Workflows**: Approval chains, audit trails, and automated change management
- **Security Analysis**: Graph-based security modeling with factor scoring for risk assessment
- **Data Auditing**: Near-live data transformation tracking for compliance and security gap identification

## Core Concepts

### Connector Architecture

Syntra uses a **connector-agnostic** design based on the Factory/Builder pattern:

```
┌─────────────────────────────────────────────────────────────────┐
│                        Syntra Core                              │
│                                                                 │
│  ┌──────────┐    ┌──────────────┐    ┌───────────────────────┐  │
│  │ Handler  │───▶│InstanceBuilder│───▶│ Dynamic Assembly Load │  │
│  └──────────┘    └──────────────┘    └───────────────────────┘  │
│       │                                        │                │
│       ▼                                        ▼                │
│  ┌──────────┐                          ┌──────────────┐         │
│  │  Config  │                          │  IProvider /  │         │
│  │  (YAML)  │                          │  IConsumer    │         │
│  └──────────┘                          └──────────────┘         │
└─────────────────────────────────────────────────────────────────┘
        │                                        │
        ▼                                        ▼
┌───────────────┐                    ┌───────────────────┐
│  Source        │                    │  Destination       │
│  ─────────     │                    │  ────────────      │
│  Active Dir    │                    │  Azure SQL         │
│  Entra ID      │                    │  Active Directory  │
│  Azure Res     │                    │  Schema Designer   │
│  SQL / Oracle  │                    │                    │
│  ServiceNow    │                    │                    │
│  Exchange      │                    │                    │
│  DevOps        │                    │                    │
│  Tenable       │                    │                    │
│  Tufin         │                    │                    │
│  Infoblox      │                    │                    │
│  CMDB          │                    │                    │
│  Ciphersuite   │                    │                    │
└───────────────┘                    └───────────────────┘
```

### Provider (Source) Interface

```csharp
public interface IProvider
{
    byte[]? Metadata { get; }
    IAsyncEnumerable<IEntity> RetrieveAsync(
        IEnumerable<string> properties,
        CancellationToken cancellationToken = default);
}

public interface IProviderBuilder
{
    void AddConfiguration(byte[] configuration);
    void AddMetadata(byte[]? metadata);
    void AddDeserializer(Func<byte[], Type, object> deserializer);
    void AddLogger(ILogger logger);
    void AddDiscloser(Func<string, char[]> discloser);
    IProvider Build();
}
```

### Consumer (Destination) Interface

```csharp
public interface IConsumer
{
    bool HadErrors { get; }
    Task SynchronizeAsync(IProvider provider, CancellationToken cancellationToken = default);
    Task FinalizeAsync(IProvider provider, CancellationToken cancellationToken = default);
}

public interface IConsumerBuilder
{
    void AddConfiguration(byte[] configuration);
    void AddDeserializer(Func<byte[], Type, object> deserializer);
    void AddLogger(ILogger logger);
    void AddDiscloser(Func<string, char[]> discloser);
    IConsumer Build();
}
```

### Entity Model

Every synchronized record is wrapped in an `IEntity`:

```csharp
public interface IEntity
{
    string? Identifier { get; }
    EntityState State { get; set; }  // Unclassified, Created, Updated, Deleted
    IDictionary<string, object?> Properties { get; }
}
```

## Synchronization Modes

### Full Sync
1. Provider streams **all** entities from source as an `IAsyncEnumerable<IEntity>`
2. Consumer processes each entity (INSERT/UPDATE via MERGE)
3. Consumer runs `FinalizeAsync()` which marks unmatched destination rows as deleted
4. Threshold enforcement prevents accidental mass deletion

### Delta Sync
1. Provider receives previous metadata (e.g., USN, DateTime offset, delta token)
2. Provider streams only **changed** entities since last sync
3. Consumer processes changed entities only
4. `FinalizeAsync()` is skipped (no deletion reconciliation)
5. Updated metadata stored for next run

### Streaming and Parallelism
The pipeline is asynchronous end to end. `RetrieveAsync` yields entities as pages arrive
from the source rather than materializing the full result set, and consumers await that
stream directly. Consumers that support concurrent writes drive the stream with
`Parallel.ForEachAsync`, bounded by the connector's `MaxDegreeOfParallelism` setting;
consumers whose target requires ordered or single-threaded writes use `await foreach`.
A `CancellationToken` flows from the host through the consumer into the provider, so a
service shutdown or Ctrl+C stops an in-flight sync at the next await point.

The LDAP library is the one place where the underlying SDK does not offer a Task-based API:
`System.DirectoryServices.Protocols` exposes only the APM `BeginSendRequest`/`EndSendRequest`
pair. `LdapConnectionExtensions.SendRequestAsync` bridges it to `await` and maps cancellation
onto `LdapConnection.Abort`. Bind remains synchronous — the SDK has no asynchronous bind, and
wrapping it would move the block to a thread-pool thread without gaining scalability.

### Delta Mechanisms by Connector
| Connector | Delta Key | Mechanism |
|-----------|-----------|-----------|
| Active Directory | USN | `uSNChanged >= previousUSN` filter |
| Entra ID | Delta Token | Microsoft Graph `/delta` endpoint |
| SQL Sources | DateTime Offset | `WHERE ModifiedDate > @lastSync` |
| ServiceNow/CMDB | DateTime Offset | REST API date filter |
| Azure Resources | N/A | Full sync only |

## Host Patterns

Syntra's synchronization engine is **host agnostic**:

### 1. Console Application (`PenguinConverters.Syntra.Host.Console`)
- Single-run execution from command line
- Args: `--configuration={file.yaml} [--schema]`
- `--schema` enables SchemaDesigner mode (output-only, no database writes)

### 2. Windows Service / Linux Daemon (`PenguinConverters.Syntra.Host.Service`)
- Long-running background service
- Cron-based scheduling via NCrontab
- Loads multiple configurations from `Configuration/` directory
- Lease file locking prevents concurrent execution
- Cross-platform: `.UseWindowsService()` + `.UseSystemd()`

### 3. REST API (`PenguinConverters.Syntra.Api`)
- ASP.NET Core Web API
- OBO (On-Behalf-Of) flow: user identity passed to SQL Database
- OData query support ($filter, $orderby, $top, $skip, $select)
- Dynamic $metadata endpoint (EDM/CSDL)
- Dynamic OpenAPI endpoint (generated from database schema)
- HTTP methods map to stored procedures: `SP_S1FE_{Entity}_READ/CREATE/UPDATE/DELETE`

## Database Schema

### Naming Conventions
- Primary Key: `{TableName}Id` (UNIQUEIDENTIFIER, DEFAULT NEWID())
- Identity: `{TableName}Identity` (INT IDENTITY)
- Audit: `{TableName}Inserted`, `{TableName}InsertedBy`, `{TableName}Updated`, `{TableName}UpdatedBy`
- Soft Delete: `{TableName}Deleted` (DATETIME2 NULL)
- Concurrency: `{TableName}RowVersion` (ROWVERSION)

### Core Schema (S1 prefix)

#### Transaction & Workflow
- **S1Transaction** - Pending operations with predecessor chaining
- **S1TransactionOperation** - Operation definitions (target, method, URI)
- **S1TransactionResult** - Execution results with error tracking
- **S1Approval** - Approval workflow definitions with dynamic approver selection
- **S1ApprovalRequest** - Approval instances with state machine
- **S1ApprovalState** - State enum (New, Approved, Rejected, Expired, Cancelled)

#### Identity & Organization
- **S1Identity** - User/identity records (SCIM RFC-7643 compliant)
- **S1OrganizationalUnit** - Organizational hierarchy
- **S1Role** / **S1RoleType** - Role definitions and types
- **S1Relationship** / **S1RelationshipType** - Generic entity relationships

#### Infrastructure
- **S1Target** / **S1TargetType** / **S1TargetNamespace** - Target system registry
- **S1Streamer** - Stored SQL queries for report generation
- **S1Shadow** - JSON shadow copies for change detection
- **S1Messaging** - Internal messaging system

### Security Graph Schema (S1 prefix)
- **S1Node** - Security graph nodes with Confidentiality/Availability factors
- **S1Edge** - Graph relationships
- **S1EdgeType** - Edge type definitions with factor weights

### Frontend Metadata
Stored procedures return dynamic UI definitions:
- `SP_S1FE_{Entity}_READ` with `@metadata` parameter controls response type
- `FN_DBObjectParameters` returns procedure parameter metadata
- `FN_DBObjectDefinitionJson` returns JSON field definitions
- Metadata levels: 0=data, 1=minimal, 2=properties, 3=count, 4=parameters, 8=field definitions

## Credential Management

All credentials use the **Keyra SDK** (`PenguinConverters.Keyra`), a separately licensed
proprietary component distributed as a NuGet package. See [README](README.md#third-party-components).

```csharp
// Encrypt a credential
using var decryptor = new DecryptorBuilder()
    .UseKeyFile("path/to/key.keyra")
    .WithPassword("vaultPassword")
    .Build();

string ciphertext = decryptor.Encrypt("myDatabasePassword");

// Decrypt at runtime
char[] plaintext = decryptor.Decrypt(ciphertext);
```

Sensitive values are held in `PenguinConverters.Keyra.SecureBuffer<T>` rather than plain
strings. Protected values integrate with configuration:
```yaml
Source:
  ConnectionString:
    Value: "BASE64_ENCRYPTED_STRING"
    Protected: true
  Password:
    Value: "plaintext-for-dev"
    Protected: false
```

## Security Features

### Schema Designer
Analyzes entity structures to infer SQL schemas:
- Detects data types (numeric, boolean, datetime, string, unicode)
- Calculates MaxLength for string columns
- Generates T-SQL CREATE TABLE statements
- Outputs JSON definition for documentation

### Security Graph (S1)
- Models security relationships as directed graph
- **CFactor** (Confidentiality) and **AFactor** (Availability) scoring
- Inherited factor calculation via recursive CTE
- Identifies lateral movement paths across systems

### API Security
- Windows Negotiate authentication (Kerberos/NTLM)
- JWT Bearer with OBO flow for Azure AD
- SQL impersonation: queries run under caller's identity
- Database functions provide per-record authorization via views

## Solution Organization

Each component is an independent Solution that can be published as a NuGet package:

| Solution | Type | Purpose |
|----------|------|---------|
| PenguinConverters.Syntra.Core | Library | Core interfaces, entities, configuration, builders |
| PenguinConverters.Syntra.ActiveDirectory | Library | LDAP operations library |
| PenguinConverters.Syntra.Api | Web API | REST API with OData/OpenAPI |
| PenguinConverters.Syntra.Host.Console | Console App | CLI synchronization runner |
| PenguinConverters.Syntra.Host.Service | Worker Service | Background service with scheduling |
| PenguinConverters.Syntra.Provider.* | Library | Source connectors (one per system) |
| PenguinConverters.Syntra.Consumer.* | Library | Destination connectors |
| PenguinConverters.Syntra.Consumer.MicrosoftSQL.SharedSchema | SSDT | Shared SQL schema (SQL 2016+, DSP Sql130) - all S1 core tables, views, functions, stored procedures |
| PenguinConverters.Syntra.Consumer.AzureSQL.Schema | SSDT | Azure SQL specific extensions (DSP Sql160) - inherits SharedSchema via .dacpac reference |
