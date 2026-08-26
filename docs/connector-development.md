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

using PenguinConverters.Keyra.Settings;
using PenguinConverters.Syntra.Core.Settings;

public class Configuration
{
    /// <summary>Host URL or server name.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>API endpoint or base path.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Authentication credentials, plaintext or Keyra-protected.</summary>
    public Secret? ClientId { get; set; }
    public Secret? ClientSecret { get; set; }

    /// <summary>Enable delta synchronization.</summary>
    public bool Delta { get; set; }

    /// <summary>Maximum parallel processing degree.</summary>
    public int MaxDegreeOfParallelism { get; set; } = 1;
}
```

### Step 4: Provider Implementation

Implement `IProvider` via the base class:

`RetrieveAsync` is an async iterator: yield each entity as it arrives from the source
instead of collecting the whole result set first. Annotate the token with
`[EnumeratorCancellation]` so `await foreach` can pass its own token through.

```csharp
namespace PenguinConverters.Syntra.Provider.{SystemName};

using System.Runtime.CompilerServices;
using PenguinConverters.Syntra.Core.Entities;
using PenguinConverters.Syntra.Core.Source;

public class Provider : Core.Source.Provider, IProvider
{
    private Configuration _configuration = new();

    public override byte[]? Metadata { get; protected set; }

    public override async IAsyncEnumerable<IEntity> RetrieveAsync(
        IEnumerable<string> properties,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 1. Deserialize configuration
        // 2. Connect to source system
        // 3. If delta: apply offset filter from metadata
        // 4. Yield IEntity for each record as each page arrives
        // 5. Update Metadata with new offset

        List<string> propertyList = properties.ToList();

        // Example: page through an API
        // HttpClient client = new HttpClient();
        // while (nextLink is not null)
        // {
        //     HttpResponseMessage response = await client.GetAsync(nextLink, cancellationToken);
        //     foreach (IEntity entity in ParsePage(response)) yield return entity;
        // }

        yield break;
    }
}
```

Resolve credentials through the base class's `TryDisclose`, which handles both plaintext and
Keyra-protected values and fails cleanly when the key is missing. Clear the characters once
you have used them — the array is yours:

```csharp
if (!TryDisclose(_configuration.ClientSecret, out char[] secretChars))
    throw new InvalidOperationException("The client secret could not be disclosed.");

try
{
    Authenticate(_configuration.ClientId, secretChars);
}
finally
{
    Array.Clear(secretChars, 0, secretChars.Length);
}
```

`TryDisclose` returns `false` rather than handing back ciphertext when a value is protected and
no key is available, so a misconfigured vault reports itself instead of surfacing later as a
rejected credential.

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

    // The pipeline owns the decryptor and disposes it; the provider only borrows it.
    public void AddDecryptor(Decryptor decryptor)
    {
        _provider.SetDecryptor(decryptor);
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

    public override async Task SynchronizeAsync(
        IProvider provider,
        CancellationToken cancellationToken = default)
    {
        IAsyncEnumerable<IEntity> entities = provider.RetrieveAsync(GetProperties(), cancellationToken);

        await foreach (IEntity entity in entities)
        {
            await UpdateEntityAsync(entity, cancellationToken);
        }
    }

    public async ValueTask UpdateEntityAsync(
        Core.Entities.IEntity entity,
        CancellationToken cancellationToken = default)
    {
        // Write entity to target system
    }

    public override async Task FinalizeAsync(
        IProvider provider,
        CancellationToken cancellationToken = default)
    {
        // Reconcile deletions (full sync only)
    }
}
```

### Writing in Parallel

When the target system tolerates concurrent writes, drive the provider stream with
`Parallel.ForEachAsync` instead of `await foreach`. `UpdateEntityAsync` already matches
the `Func<T, CancellationToken, ValueTask>` body signature, so it can be passed directly.
Bound the concurrency with the connector's `MaxDegreeOfParallelism` setting and keep any
shared state in a concurrent collection — this is what `Consumer.AzureSQL` does:

```csharp
public override async Task SynchronizeAsync(
    IProvider provider,
    CancellationToken cancellationToken = default)
{
    await Parallel.ForEachAsync(
        provider.RetrieveAsync(GetProperties(), cancellationToken),
        new ParallelOptions
        {
            MaxDegreeOfParallelism = _configuration.MaxDegreeOfParallelism,
            CancellationToken = cancellationToken
        },
        UpdateEntityAsync);
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
# Locates the Keyra key that opens every `Protected: true` value below. Omit the section
# entirely when the configuration holds no protected values. The key password is not kept
# here — it comes from SYNTRA_KEYRA_PASSWORD, or from the variable PasswordVariable names.
Keyra:
  KeyFile: "D:/secure/syntra.keyra"
  # …or, with no key file on disk, name the variable holding an armored share:
  # ShareVariable: "SYNTRA_KEYRA_SHARE"
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
