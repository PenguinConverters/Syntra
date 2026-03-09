# Syntra

**Syntra** is an IAM (Identity & Access Management) and Automation framework by **Penguin Converters AG**.

It reads from multiple sources (databases, APIs, directories) and writes to multiple destinations using a connector-agnostic architecture. Syntra supports full and delta synchronization, transaction workflows with approval chains, and exposes a dynamic REST API with OData and OpenAPI support.

## Key Features

- **Connector-Agnostic Synchronization** - Plugin-based providers (sources) and consumers (destinations) loaded dynamically via reflection
- **Full & Delta Sync** - USN tracking for Active Directory, delta tokens for Microsoft Graph, DateTime offset for APIs
- **Host Agnostic** - Runs as CLI tool, Windows Service, Linux Daemon (systemd), or Azure Function
- **Dynamic REST API** - OData query parameters, OBO (On-Behalf-Of) authentication, per-record SQL authorization
- **Dynamic $metadata** - EDM/CSDL schema generated at runtime from database
- **Dynamic OpenAPI** - OpenAPI 3.0 specification generated at runtime without compilation
- **Transaction Workflows** - Approval chains with dynamic approver selection, audit trails
- **Security Graph** - Graph-based security modeling with confidentiality/availability factor scoring
- **Schema Designer** - Automatic SQL schema inference from source data
- **Credential Protection** - Keyra SDK integration for encrypted configuration

## Supported Connectors

### Providers (Sources)
| Connector | System | Delta |
|-----------|--------|-------|
| ActiveDirectory | On-premises AD (LDAP) | USN |
| EntraID | Microsoft Entra ID | Graph delta |
| AzureResources | Azure Resource Manager | Full |
| AzureSQL | SQL Server / Azure SQL | DateTime |
| ServiceNow | ServiceNow ITSM | DateTime |
| CMDB | Generic CMDB | DateTime |
| Exchange | Exchange Online | Graph |
| DevOps | Azure DevOps | REST |

### Consumers (Destinations)
| Connector | System |
|-----------|--------|
| AzureSQL | SQL Server / Azure SQL (includes platform schema) |
| ActiveDirectory | On-premises AD (LDAP write-back) |
| SchemaDesigner | Schema inference and T-SQL generation |

## Solution Structure

Each component is an independent solution publishable as a NuGet package:

```
PenguinConverters.Syntra.Core/                  # Core interfaces, entities, configuration
PenguinConverters.Syntra.ActiveDirectory/        # LDAP operations library
PenguinConverters.Syntra.Api/                    # REST API (OData, OpenAPI, $metadata)
PenguinConverters.Syntra.Host.Console/           # CLI application
PenguinConverters.Syntra.Host.Service/           # Windows Service / Linux Daemon
PenguinConverters.Syntra.Provider.*/             # Source connectors
PenguinConverters.Syntra.Consumer.*/             # Destination connectors
```

## Quick Start

```bash
# Build a specific solution
dotnet build PenguinConverters.Syntra.Core/PenguinConverters.Syntra.Core.sln

# Run a one-time sync
dotnet run --project PenguinConverters.Syntra.Host.Console/PenguinConverters.Syntra.Host.Console/ -- --configuration=config.yaml

# Run schema discovery
dotnet run --project PenguinConverters.Syntra.Host.Console/PenguinConverters.Syntra.Host.Console/ -- --configuration=config.yaml --schema
```

## Documentation

- [Architecture](ARCHITECTURE.md) - System design and patterns
- [User Guide](docs/user-guide.md) - Installation and usage
- [API Reference](docs/api-reference.md) - REST API documentation
- [Connector Development](docs/connector-development.md) - Building new connectors

## Requirements

- .NET 8.0 SDK
- SQL Server 2017+ (for Consumer.AzureSQL)
- Keyra SDK (for credential management)

## License

Copyright (c) Penguin Converters AG. All rights reserved.
