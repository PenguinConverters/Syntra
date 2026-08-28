# Syntra - Naming Conventions & Schema Standards

This document is the **golden standard** for Syntra's naming conventions. All future extensions, connectors, database objects, and code must follow these rules.

## 1. Namespace Conventions

### Root Namespace
```
PenguinConverters.Syntra
```

### Component Namespaces
| Pattern | Example | Purpose |
|---------|---------|---------|
| `PenguinConverters.Syntra.Core` | Core library | Interfaces, entities, configuration, builders |
| `PenguinConverters.Syntra.Provider.{System}` | `Provider.EntraID` | Source connector (reads data) |
| `PenguinConverters.Syntra.Consumer.{System}` | `Consumer.AzureSQL` | Destination connector (writes data) |
| `PenguinConverters.Syntra.Host.{Type}` | `Host.Service` | Execution host (Console, Service) |
| `PenguinConverters.Syntra.{Library}` | `ActiveDirectory` | Shared library |

### Rules
- Provider = **Source** (reads from external system)
- Consumer = **Destination** (writes to external system)
- A system can be both a Provider AND a Consumer (e.g., ActiveDirectory, AzureSQL)
- All namespaces must be rooted at `PenguinConverters.Syntra`; no other root is permitted

## 2. Database Naming Conventions

### Schema Prefixes

| Prefix | Domain | Description |
|--------|--------|-------------|
| `S1` | Syntra Core | Platform tables: transactions, approvals, identities, roles, relationships |
| `S1` | Security Graph | Security graph: nodes, edges, factor scoring |
| `S1FE` | Frontend | Stored procedures that return UI definitions and data |

Future extensions should use new prefixes (e.g., `C0` for compliance, `A0` for auditing) to maintain namespace separation.

### Table Naming

**Format:** `{Prefix}{EntityName}`

| Convention | Example | Description |
|------------|---------|-------------|
| Core table | `S1Transaction` | Platform entity |
| Enum/lookup table | `S1ApprovalState` | Static lookup values |
| Junction table | `S1ApprovalRequestApprover` | Many-to-many relationship |
| Synced entity table | `ADUser` | Data imported from connector |
| Relationship table | `S1Relationship` | Generic entity-to-entity link |

### Column Naming

**Standard Columns (EVERY table must have these):**

| Column | Type | Default | Purpose |
|--------|------|---------|---------|
| `{Table}Id` | `UNIQUEIDENTIFIER` | `NEWID()` | Primary key |
| `{Table}Identity` | `INT IDENTITY(0,1)` | Auto-increment | Sequential identifier |
| `{Table}Inserted` | `DATETIME2` | `GETUTCDATE()` | Record creation timestamp |
| `{Table}InsertedBy` | `VARCHAR(128)` | `SUSER_SNAME()` | Creator identity |
| `{Table}Updated` | `DATETIME2` | `GETUTCDATE()` | Last modification timestamp |
| `{Table}UpdatedBy` | `VARCHAR(128)` | `SUSER_SNAME()` | Last modifier identity |
| `{Table}Deleted` | `DATETIME2 NULL` | `NULL` | Soft-delete timestamp (NULL = active) |
| `{Table}RowVersion` | `ROWVERSION` | Auto | Concurrency control |

**Example for `S1Transaction`:**
```sql
[S1TransactionId]         UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
[S1TransactionIdentity]   INT IDENTITY(0,1) NOT NULL,
[S1TransactionInserted]   DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
[S1TransactionInsertedBy] VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
[S1TransactionUpdated]    DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
[S1TransactionUpdatedBy]  VARCHAR(128) NOT NULL DEFAULT SUSER_SNAME(),
[S1TransactionDeleted]    DATETIME2 NULL,
[S1TransactionRowVersion] ROWVERSION NOT NULL
```

**Entity-Specific Columns:**
- Business columns use **camelCase** or **PascalCase** matching the source system
- Foreign keys: `{ReferencedTable}Id` (e.g., `S1ApprovalId`)
- Flexible payload: `ObjectId01-04` (VARCHAR), `PropertyJson01-04` (VARCHAR MAX), `PropertyText01-02` (NVARCHAR MAX), `PropertyHtml01` (VARCHAR MAX)

### Synced Entity Table Columns

Tables created by `Consumer.AzureSQL` for synced data follow this pattern:

| Column | Type | Purpose |
|--------|------|---------|
| `ObjectId` | `VARCHAR(36)` | Auto-generated PK (GUID) |
| `id` | `VARCHAR(36) UNIQUE` | Source system's object ID |
| `{property1..N}` | Various | Mapped source properties |
| `{Table}Shadow` | `NVARCHAR(MAX)` | JSON snapshot of complete source object |
| `{Table}Updated` | `DATETIME2` | Last sync update |
| `{Table}Inserted` | `DATETIME2` | First sync insert |
| `{Table}Deleted` | `DATETIME2 NULL` | Soft-delete (NULL = active) |
| `{Table}RowVersion` | `ROWVERSION` | Concurrency control |

### Trigger Naming

**Format:** `Trigger_{Table}_{Column}-{Operation}`

```sql
CREATE TRIGGER [dbo].[Trigger_S1Transaction_S1TransactionUpdated-UPDATE]
    ON [dbo].[S1Transaction]
    AFTER UPDATE
    AS BEGIN
        UPDATE [dbo].[S1Transaction]
        SET [S1TransactionUpdated] = GETUTCDATE(),
            [S1TransactionUpdatedBy] = SUSER_SNAME()
        WHERE [S1TransactionId] IN (SELECT DISTINCT [S1TransactionId] FROM Inserted)
    END
```

### View Naming

| Pattern | Example | Purpose |
|---------|---------|---------|
| `S1Table` | System views | Table metadata (MD5-hashed IDs) |
| `S1Schema` | System views | Column metadata |
| `VW_{Prefix}{Entity}` | `VW_S1Edge` | Extended data view |
| `VX_{Prefix}{Entity}` | `VX_S1Edge` | Cross-reference view |
| `VA_{Entity}` | `VA_AZUser_RiskScore` | Analytical/aggregation view |
| `VW_CSR_{Entity}` | `VW_CSR_AZGovernanceRoleAssignment` | Compliance reporting view |

### Function Naming

| Pattern | Example | Purpose |
|---------|---------|---------|
| `FN_{Prefix}{Description}` | `FN_S1GenerateTableScript` | Domain-specific function |
| `FN_DBObject{Description}` | `FN_DBObjectParameters` | Database metadata function |
| `FN_{Entity}_{Property}` | `FN_ADGroupType` | Entity-specific calculation |

### Stored Procedure Naming

| Pattern | Example | Purpose |
|---------|---------|---------|
| `SP_{Table}_CREATE` | `SP_S1Transaction_CREATE` | Insert operation |
| `SP_{Table}_READ` | `SP_S1Identity_READ` | Select operation |
| `SP_{Table}_UPDATE` | `SP_S1TransactionTrigger_UPDATE` | Update operation |
| `SP_{Table}_DELETE` | `SP_AZSignIn_DELETE` | Delete/cleanup operation |
| `SP_{Table}_MERGE` | `SP_S1Node_From_ADUser_MERGE` | Sync MERGE operation |
| `SP_{Table}_{Action}_UPDATE` | `SP_S1Node_AFactorInherited_UPDATE` | Specific action |

### Frontend Stored Procedures (SP_S1FE_*)

These procedures power the dynamic frontend. They follow a strict naming convention:

**Format:** `SP_S1FE_{Entity}_{View}_READ` or `SP_S1FE_{Entity}_CREATE/UPDATE/DELETE`

| HTTP Method | Procedure | Example |
|-------------|-----------|---------|
| GET | `SP_S1FE_{Entity}_READ` | `SP_S1FE_S1Identity_READ` |
| POST | `SP_S1FE_{Entity}_CREATE` | `SP_S1FE_S1Identity_CREATE` |
| PATCH | `SP_S1FE_{Entity}_UPDATE` | `SP_S1FE_S1Identity_UPDATE` |
| DELETE | `SP_S1FE_{Entity}_DELETE` | `SP_S1FE_S1Identity_DELETE` |

**The `@metadata` Parameter:**

Every `SP_S1FE_*_READ` procedure accepts `@metadata INT = 0`:

| Value | Returns | Use Case |
|-------|---------|----------|
| 0 | Data rows | Default - actual entity data |
| 1 | Minimal metadata | Basic response metadata |
| 2 | Property definitions | Field types, display names, FK relationships |
| 3 | Count | Total record count |
| 4 | Parameter definitions | SP parameter metadata via `FN_DBObjectParameters` |
| 8 | Full field definitions | DataType, DisplayName, RegularExpression, FilterBy, Hidden, IsActive |

**Frontend Field Definition Format (metadata=8):**

```json
{
  "fieldName": {
    "DataType": "nvarchar",
    "DisplayName": "Display Name",
    "MaxLength": 256,
    "IsNullable": true,
    "RegularExpression": "^[a-zA-Z0-9@.]+$",
    "FilterBy": true,
    "Hidden": false,
    "IsActive": true
  }
}
```

**Frontend Container Format (forms with buttons):**

```json
{
  "Parameters": {
    "@FieldName": {
      "DisplayName": "Field Label",
      "Type": "nvarchar",
      "MaxLength": 500,
      "Value": null,
      "IsNullable": false
    }
  },
  "Buttons": [
    {
      "DisplayName": "Submit",
      "Action": "CREATE",
      "Method": "POST",
      "Parameters": ["@FieldName"]
    }
  ]
}
```

## 3. Code Naming Conventions

### C# Standards
- **Always use explicit types** - never `var`
- Namespaces match folder structure
- Interfaces prefixed with `I` (e.g., `IProvider`, `IConsumer`)
- Builder classes suffixed with `Builder` (e.g., `ProviderBuilder`, `ConnectionBuilder`)
- Configuration classes named `Configuration` in their respective namespace
- Test classes suffixed with `Tests` (e.g., `EntityTests`, `ConnectionBuilderTests`)

### Member Regions

Every type groups its members into `#region` blocks named after the **member type**, so any
file can be collapsed to its structure and scanned the same way. Emit only the regions a type
actually needs — never an empty region — and use this order:

| Order | Region | Contains |
|-------|--------|----------|
| 1 | `#region Constants` | `const` fields |
| 2 | `#region Fields` | instance and `static readonly` fields |
| 3 | `#region Constructors` | instance constructors, static constructors, finalizers |
| 4 | `#region Properties` | properties, including auto-properties |
| 5 | `#region Indexers` | `this[...]` members |
| 6 | `#region Events` | `event` members |
| 7 | `#region Methods` | all methods, including operators and `[GeneratedRegex]` partials |
| 8 | `#region Nested Types` | nested classes, structs, records, enums |

Rules:
- Region names are **singular in meaning, plural in form** and spelled exactly as above,
  in Title Case. `Nested Types` takes two words.
- Applies to classes, structs, records and interfaces. Enums and file-scoped top-level
  statements take no regions — they have a single member kind.
- Where a file declares several types, each type carries its own regions.
- Blank line after `#region` and before `#endregion`.
- A type whose layout is driven by the contracts it implements rather than by member kind —
  `QuickDictionary` is the one such case — may keep contract-named regions instead
  (`Enumeration`, `Non-generic IDictionary`), because splitting them by member type would
  scatter each interface implementation.

```csharp
public class Handler
{
    #region Fields

    private readonly ILogger _logger;

    #endregion

    #region Constructors

    internal Handler(ILogger logger) => _logger = logger;

    #endregion

    #region Methods

    public Task SynchronizeAsync(CancellationToken cancellationToken = default) { }

    #endregion
}
```

### Interface Pattern
```
IProvider         → Provider (abstract base) → {System}Provider (concrete)
IProviderBuilder  → ProviderBuilder (concrete per system)
IConsumer         → Consumer (abstract base) → {System}Consumer (concrete)
IConsumerBuilder  → ConsumerBuilder (concrete per system)
```

### Configuration File Naming
| Format | Use Case | Location |
|--------|----------|----------|
| `{description}.yaml` | Service/Console sync job | `Configuration/` directory |
| `{description}.json` | Service/Console sync job (alternative) | `Configuration/` directory |
| `appsettings.json` | API configuration | API project root |

### Assembly Naming
Every assembly follows the namespace: `PenguinConverters.Syntra.{Component}.dll`

### Folder and Project-File Naming
Folder and project-file names carry the **short form** — the component name with the
`PenguinConverters.Syntra.` prefix removed. The full identity lives in `AssemblyName` and
`RootNamespace`, which every project sets explicitly:

| On disk | `AssemblyName` / `RootNamespace` |
|---------|----------------------------------|
| `Core/Core.csproj` | `PenguinConverters.Syntra.Core` |
| `Provider.EntraID/Provider.EntraID.csproj` | `PenguinConverters.Syntra.Provider.EntraID` |
| `Core.Tests/Core.Tests.csproj` | `PenguinConverters.Syntra.Core.Tests` |

Do not "fix" this by lengthening folder names. The tier prefix (`Provider.`, `Consumer.`,
`Host.`) sorts related projects together in a flat solution, which is what replaces a folder
tree. Database projects additionally set `SqlTargetName` so the `.dacpac` keeps the full name.

The hosts are the deliberate exceptions. An executable's assembly name is what an operator
types or writes into a service definition, so it is short:

| Project | Ships as |
|---------|----------|
| `Host.Console` | `CMDSYNTRA.exe` |
| `Host.Service` | `svcsyntra.exe` |

Only an executable may do this. `Directory.Build.targets` fails the build of any library whose
assembly name does not begin with `PenguinConverters.Syntra.` (`SYNTRA0001`).

## 4. API Naming Conventions

### Route Pattern
```
api/{entity}                  → Data operations (GET/POST/PATCH/DELETE)
api/$metadata                 → OData EDM/CSDL schema
api/openapi.json              → Dynamic OpenAPI specification
```

### OData Parameter Prefix
Standard OData: `$filter`, `$orderby`, `$top`, `$skip`, `$skiptoken`, `$select`, `$apply`
Custom Syntra: `@syntra.metadata`, `@syntra.previousLink`

### Response Properties
```json
{
  "@odata.context": "api/$metadata#{Entity}",
  "@odata.count": 1234,
  "@odata.nextLink": "api/{Entity}?$skiptoken=...",
  "value": [...]
}
```

## 5. Security Graph Naming (S1 Schema)

### Factor Scoring
| Factor | Column | Description |
|--------|--------|-------------|
| CFactor | `CFactor` / `CFactorInherited` | Confidentiality factor (0-255) |
| AFactor | `AFactor` / `AFactorInherited` | Availability factor (0-255) |

### Edge Types
Named as verbs describing the relationship:
- `isMemberOf` - Group membership
- `isTokenExposureTo` - Credential exposure path
- `isAssignedTo` - Role/permission assignment

### Node Types
Match the source table name: `ADUser`, `ADGroup`, `ADComputer`, `AZServicePrincipal`

## 6. Adding New Components

### New Provider
1. Create `Provider.{System}/` at repo root containing `Provider.{System}.csproj`
2. Set `RootNamespace` and `AssemblyName` to `PenguinConverters.Syntra.Provider.{System}`
3. Files: `Provider.cs`, `ProviderBuilder.cs`, `Source/Configuration.cs`
4. Add the project to `Syntra.slnx`
5. Reference Core with `<ProjectReference Include="..\Core\Core.csproj" />`

### New Consumer
Same pattern with `Consumer.{System}/` and `Target/Configuration.cs`

### New Database Tables
1. Add to `Consumer.AzureSQL.Database` under `Tables/`
2. Follow the `{Prefix}{Entity}` naming
3. Include ALL standard columns (Id, Identity, Inserted, InsertedBy, Updated, UpdatedBy, Deleted, RowVersion)
4. Create UPDATE trigger for timestamp maintenance
5. Add a `<Build Include="..." />` item to the `.sqlproj` - a Visual Studio SSDT project lists its
   files explicitly. Visual Studio does this for you when you add the file through the IDE.

### Database Project Architecture

`Consumer.AzureSQL.Database` is a **Visual Studio SSDT project** (`ToolsVersion 4.0`, DSP Sql170,
`TargetFrameworkVersion v4.7.2`). It is the single database project: tables, views and functions,
building to a `.dacpac`.

**It builds with `MSBuild.exe`, not `dotnet build`.** It imports SSDT targets that ship with
Visual Studio, so `dotnet build` fails with `MSB4278`. Consequences:

- `Syntra.slnx` is the Visual Studio solution and contains it.
- CI and `dotnet build` use `Syntra.CI.slnf`, a solution filter covering the C# projects only.
  Add new C# projects to both files.
- **Do not convert it to `Microsoft.Build.Sql` SDK style.** That format needs the SDK-style SQL
  project system (`MSBuild\Sdks\Microsoft.SqlProject.Sdk`), which is absent from a stock Visual
  Studio install. Without it the project does not load in the IDE at all - the symptom is
  *"The item '...' does not exist in the project directory; Document load is being skipped"*.
- `Directory.Build.targets` rules for SQL projects are gated on `UsingMicrosoftBuildSqlSdk`, so
  they never touch this project. An SSDT project sets its own `TargetFrameworkVersion`; imposing
  one from `Directory.Build.*` breaks it with `MSB3644`.

### New Frontend Procedures
1. Create `SP_S1FE_{Entity}_READ` with `@metadata` parameter
2. Support metadata levels 0, 3, 4 (minimum), ideally all levels
3. Use `FN_DBObjectParameters` for metadata=4
4. Add to `Consumer.AzureSQL.Database` under `Stored Procedures/`
