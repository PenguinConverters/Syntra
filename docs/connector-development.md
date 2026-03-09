# Syntra - Connector Development Guide

## Overview

Syntra uses a plugin-based connector architecture. Each connector is an independent assembly loaded dynamically at runtime via reflection. This guide covers how to build new **Providers** (sources) and **Consumers** (destinations).

## Provider (Source Connector)

A Provider reads data from an external system and yields entities.

### Step 1: Create Solution

Create a new solution folder at the repository root:

```
PenguinConverters.Syntra.Provider.{SystemName}/
├── PenguinConverters.Syntra.Provider.{SystemName}/
│   ├── PenguinConverters.Syntra.Provider.{SystemName}.csproj
│   ├── Provider.cs
│   ├── ProviderBuilder.cs
│   └── Source/
│       └── Configuration.cs
└── PenguinConverters.Syntra.Provider.{SystemName}.sln
```

### Step 2: Project File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\PenguinConverters.Syntra.Core\PenguinConverters.Syntra.Core\PenguinConverters.Syntra.Core.csproj" />
  </ItemGroup>
</Project>
```

### Step 3: Configuration

Define your source-specific settings in `Source/Configuration.cs`:

```csharp
namespace PenguinConverters.Syntra.Provider.{SystemName}.Source;

using PenguinConverters.Syntra.Core.Settings;

public class Configuration
{
    /// <summary>Host URL or server name.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>API endpoint or base path.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Authentication credentials (encrypted via Keyra).</summary>
    public ProtectedString ClientId { get; set; } = new();
    public ProtectedString ClientSecret { get; set; } = new();

    /// <summary>Enable delta synchronization.</summary>
    public bool Delta { get; set; }

    /// <summary>Maximum parallel processing degree.</summary>
    public int MaxDegreeOfParallelism { get; set; } = 1;
}
```

### Step 4: Provider Implementation

Implement `IProvider` via the base class:

```csharp
namespace PenguinConverters.Syntra.Provider.{SystemName};

using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Source;

public class Provider : Core.Source.Provider, IProvider
{
    private Configuration _configuration = new();

    public override byte[]? Metadata { get; protected set; }

    public override IEnumerable<IEntity> Retrieve(IEnumerable<string> properties)
    {
        // 1. Deserialize configuration
        // 2. Connect to source system
        // 3. If delta: apply offset filter from metadata
        // 4. Yield IEntity for each record
        // 5. Update Metadata with new offset

        List<string> propertyList = properties.ToList();

        // Example: query an API
        // HttpClient client = new HttpClient();
        // ...

        yield break;
    }
}
```

### Step 5: Provider Builder

```csharp
namespace PenguinConverters.Syntra.Provider.{SystemName};

using PenguinConverters.Syntra.Core.Source;

public class ProviderBuilder : IProviderBuilder
{
    private readonly Provider _provider = new();

    public void AddConfiguration(byte[] configuration)
    {
        _provider.SetConfiguration(configuration);
    }

    public void AddMetadata(byte[]? metadata)
    {
        _provider.SetMetadata(metadata);
    }

    public void AddDeserializer(Func<byte[], Type, object> deserializer)
    {
        _provider.SetDeserializer(deserializer);
    }

    public void AddLogger(Microsoft.Extensions.Logging.ILogger logger)
    {
        _provider.SetLogger(logger);
    }

    public void AddDiscloser(Func<string, char[]> discloser)
    {
        _provider.SetDiscloser(discloser);
    }

    public IProvider Build()
    {
        return _provider;
    }
}
```

## Consumer (Destination Connector)

A Consumer writes entities to a target system.

### Implementation Pattern

```csharp
namespace PenguinConverters.Syntra.Consumer.{SystemName};

using PenguinConverters.Syntra.Core.Source;
using PenguinConverters.Syntra.Core.Target;

public class Consumer : Core.Target.Consumer, IConsumer, ISynchronizable
{
    public override bool HadErrors { get; protected set; }

    public override void Synchronize(IProvider provider)
    {
        IEnumerable<IEntity> entities = provider.Retrieve(GetProperties());

        foreach (IEntity entity in entities)
        {
            UpdateEntity(entity);
        }
    }

    public void UpdateEntity(Core.Entities.IEntity entity)
    {
        // Write entity to target system
    }

    public override void Finalize(IProvider provider)
    {
        // Reconcile deletions (full sync only)
    }
}
```

## Delta Synchronization

### DateTime Offset Pattern (APIs)
Store the latest `ModifiedDate` as metadata bytes. On next run, filter by `ModifiedDate > previousOffset`.

### USN Pattern (Active Directory)
Store `HighestCommittedUSN` + `ServerObjectGuid`. On next run, filter by `uSNChanged >= previousUSN`.

### Delta Token Pattern (Microsoft Graph)
Store the delta token from Graph API response. On next run, pass `$deltatoken` parameter.

## Configuration File Format (YAML)

```yaml
ObjectNamespace: "MySystem.Users"
Delta: true
MaxDegreeOfParallelism: 3
Source:
  Type: "PenguinConverters.Syntra.Provider.{SystemName}"
  Host: "https://api.example.com"
  Endpoint: "/v2/users"
  ClientId:
    Value: "my-client-id"
    Protected: false
  ClientSecret:
    Value: "BASE64_ENCRYPTED"
    Protected: true
Target:
  Type: "PenguinConverters.Syntra.Consumer.AzureSQL"
  TableName: "MySystemUser"
  ConnectionString:
    Value: "BASE64_ENCRYPTED"
    Protected: true
  PrimaryKeys:
    id: "id"
  Columns:
    - id
    - displayName
    - email
    - department
Trigger:
  Crontab: "0 2 * * *"
```
