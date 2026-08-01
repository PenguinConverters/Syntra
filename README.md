# Syntra

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![Build](https://github.com/PenguinConverters/Syntra/actions/workflows/build.yml/badge.svg)](https://github.com/PenguinConverters/Syntra/actions/workflows/build.yml)
[![CodeQL](https://github.com/PenguinConverters/Syntra/actions/workflows/codeql.yml/badge.svg)](https://github.com/PenguinConverters/Syntra/actions/workflows/codeql.yml)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download)

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

- [Contributing](.github/CONTRIBUTING.md) - Build instructions, coding standards, pull request process
- [Security Policy](.github/SECURITY.md) - How to report a vulnerability

## Requirements

- .NET 8.0 SDK
- SQL Server 2016+ (for the SQL schema projects and `Consumer.AzureSQL`)
- **Keyra SDK licence and private feed access** - see below

## Third-Party Components

### Keyra SDK

`PenguinConverters.Syntra.Core` depends on the **Keyra SDK**
(`PenguinConverters.Keyra`, `PenguinConverters.Keyra.Core`) for credential protection,
and every other component depends on Core.

> **The Keyra SDK is proprietary and separately licensed.** It is **not** covered by the
> Apache-2.0 license that governs Syntra, is not published to nuget.org, and its source is
> not public. Building Syntra requires a Keyra licence and access to the private NuGet
> feed that serves these packages.

```bash
dotnet nuget add source <FEED_URL> --name keyra \
    --username <USER> --password <TOKEN> --store-password-in-clear-text
```

Nothing in Syntra's Apache-2.0 grant conveys any right to use, copy, modify, or
redistribute the Keyra SDK. See [NOTICE](NOTICE) for the full statement, and
[CONTRIBUTING.md](.github/CONTRIBUTING.md#the-keyra-sdk-dependency) for setup.

The SQL schema project (`Consumer.MicrosoftSQL.SharedSchema`) has no Keyra dependency and
builds with the .NET SDK alone.

### Other dependencies

All remaining dependencies come from nuget.org under their own licenses - Azure SDK,
Microsoft.Graph, Microsoft.Identity.Web, Microsoft.Data.SqlClient, Swashbuckle.AspNetCore,
YamlDotNet, NCrontab.Signed and NUnit among them.

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](.github/CONTRIBUTING.md) before
opening a pull request, and note that this project follows the
[Contributor Covenant](.github/CODE_OF_CONDUCT.md).

Found a security issue? **Do not open a public issue** - use
[private vulnerability reporting](https://github.com/PenguinConverters/Syntra/security/advisories/new).
See [SECURITY.md](.github/SECURITY.md).

## License

Copyright 2026 Penguin Converters AG.

Licensed under the [Apache License, Version 2.0](LICENSE). You may obtain a copy of the
License at http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software distributed under the
License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND,
either express or implied.

The Keyra SDK is excluded from this grant - see [Third-Party Components](#third-party-components).
