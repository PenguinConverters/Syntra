# Syntra - Connector Development Guide

## Overview

Syntra uses a plugin-based connector architecture. Each connector is an independent assembly loaded dynamically at runtime via reflection. This guide covers how to build new **Providers** (sources) and **Consumers** (destinations).

## Provider (Source Connector)

A Provider reads data from an external system and yields entities.

### Step 1: Create the Project

Create a project folder at the repository root. The folder name uses the short form; the
assembly keeps the full `PenguinConverters.Syntra.` identity (see
[naming conventions](naming-conventions.md#folder-and-project-file-naming)):

```
Provider.{SystemName}/
├── Provider.{SystemName}.csproj
├── Provider.cs
├── ProviderBuilder.cs
└── Source/
    └── Configuration.cs
```

Then add it to `Syntra.slnx`:

```xml
<Project Path="Provider.{SystemName}/Provider.{SystemName}.csproj" />
```

### Step 2: Project File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>PenguinConverters.Syntra.Provider.{SystemName}</RootNamespace>
    <AssemblyName>PenguinConverters.Syntra.Provider.{SystemName}</AssemblyName>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" />
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

## RESTful Provider (Inherited Source Connector)

A connector for an HTTP JSON API does not need any of the steps above. `Provider.RESTful` is a
complete connector — retrieval, paging, authentication, delta filtering, deletion marking and
child endpoints are all implemented there and described by configuration. Point `Source.Type` at
it directly, or derive from it when the API needs something configuration cannot state.

### Using it without any code

```yaml
Source:
  Type: PenguinConverters.Syntra.Provider.RESTful
  Host: api.example.com
  EndPoint: v2/assets
  IdentityProperty: id
  ResultPath: result            # where the collection sits in the response body
  Authentication:
    Mode: Basic
    Username: { Value: svc_sync }
    Password: { Value: <ciphertext>, Protected: true }
  Pagination:
    Mode: Token
    TokenPath: next_page_id
    TokenParameter: _page_id
```

### Configuration reference

| Group | Settings |
|---|---|
| Address | `BaseUrl` or `Host`/`Scheme`/`Port`, `EndPoint`, `HttpMethod`, `Body`, `ContentType`, `Accept` |
| Query | `Parameters`, `HttpHeaders` |
| Projection | `PropertiesParameter`, `PropertiesFormat`, `PropertiesSeparator`, `PropertiesToLoad`, `PropertiesToIgnore` |
| Response shape | `ResultPath`, `EntryPath`, `IdentityProperty` |
| Paging | `Pagination.Mode` = `None`/`NextLink`/`Token`/`Offset`/`Page`, plus its paths and parameters |
| Auth | `Authentication.Mode` = `None`/`Basic`/`ApiKey`/`Token`/`ClientCredentials`/`Session` |
| Delta | `Delta`, `OffsetProperty`, `FilterParameter`, `FilterFormat`, `FilterCombineFormat`, `OffsetFormat` |
| Deletion | `DeletedProperty`, `DeletedValue` |
| Nesting | `Children[]`, `ParentIdentityProperty`, `InheritParentProperties`, `Properties` |
| Transport | `RemoteCertificateValidation`, `Proxy`, `ReadRetryMaxCount`, `ReadRetryDelaySeconds`, `MaxDegreeOfParallelism`, timeouts |

A path such as `ResultPath` or `Pagination.NextLinkPath` is dot-separated, and a numeric segment
indexes an array — `_links.next.0.href`. A name that itself contains a dot, such as
`@odata.nextLink`, is matched whole before the path is split.

A child endpoint addresses its parent through a `<%property%>` placeholder:

```yaml
  EndPoint: devices
  IdentityProperty: id
  Children:
    - EndPoint: devices/<%id%>/policies
      ParentIdentityProperty: deviceId
      InheritParentProperties: true
```

Child endpoints are read concurrently up to `MaxDegreeOfParallelism`, and the entities they
produce are streamed through a bounded channel, so a slow consumer throttles the retrieval rather
than the whole result set accumulating in memory.

### Deriving a connector

Set the API's defaults in a derived `Configuration` so the configuration file carries only what
varies per installation, and name it from the provider:

```csharp
public class Configuration : RESTful.Source.Configuration
{
    public Configuration()
    {
        ResultPath = "entries";
        EntryPath = "values";
        Pagination = new PaginationSettings { Mode = PaginationMode.NextLink, NextLinkPath = "_links.next.0.href" };
    }
}

public class Provider : RESTful.Provider
{
    protected override RESTful.Source.Configuration? ReadConfiguration()
        => DeserializeConfiguration<Configuration>();
}

public class ProviderBuilder : RESTful.ProviderBuilder
{
    protected override RESTful.Provider CreateProvider() => new Provider();
}
```

**Restore nested defaults after deserialization.** A deserializer fills a fresh object rather
than merging into one, so a configuration file that mentions `Authentication` at all replaces the
whole section — silently discarding the `Mode` the constructor set, and sending anonymous
requests. Override `ApplyDefaults()`, which the provider calls once after deserialization, and
fill in only what is still unset:

```csharp
public Configuration() => ApplyCmdbDefaults();

public override void ApplyDefaults()
{
    base.ApplyDefaults();
    ApplyCmdbDefaults();
}

private void ApplyCmdbDefaults()
{
    ResultPath ??= DefaultResultPath;

    Authentication ??= new AuthenticationSettings();

    if (Authentication.Mode == AuthenticationMode.None)
        Authentication.Mode = AuthenticationMode.Session;

    Authentication.TokenEndPoint ??= DefaultLoginEndPoint;
}
```

Scalar properties do not need this — a deserializer leaves a property the file does not mention
at its constructed value. Only nested sections (`Authentication`, `Pagination`, `Proxy`) are
replaced wholesale.

### Expanding one row into many records

`ContentReader` is a stream-to-records delegate, so it is also where a one-row-to-many-records
expansion belongs — `EntryTransform` maps one record to zero or one and cannot express it.
`Provider.Tenable` uses this for Nessus scan exports, where a single row's plugin output lists
every cipher suite, certificate, SSH algorithm or SSH version a host offers:

```yaml
Source:
  Type: PenguinConverters.Syntra.Provider.Tenable
  Host: tenable.example.com
  EndPoint: rest/report/<%ReportId(Weekly Scan)%>/download
  Plugin: Nessus          # None stores the row as it stands
  IdentityProperty: MD5HashCode
```

The delegate is composable rather than baked in:

```csharp
// Use the built-in expansion from a host.
provider.ContentReader = NessusContentReader.Create(configuration, logger);

// Or expand rows some other way; nothing else about the connector changes.
provider.ContentReader = (stream, configuration, token) => MyReader.ReadAsync(stream, token);
```

An assigned `ContentReader` always wins over the configured `Plugin`.

### The six connectors built on it

| Connector | What it is |
|---|---|
| `Provider.CMDB` | Configuration + one value handler coercing the modification timestamp |
| `Provider.Tufin` | Configuration only — Basic auth, nested `ResultPath`, device → policy → rule walk |
| `Provider.Infoblox` | Configuration only — WAPI envelope, `_return_fields`, `_page_id` token paging |
| `Provider.Ciphersuite` | Configuration + an entry transform unwrapping the IANA-name-keyed record |
| `Provider.Tenable` | Configuration + a delimited content reader, a report-identifier resolver, and the Nessus expansion |
| `Provider.RESTful` | Usable directly, with no subclass at all |

### Customization seams

Never override `RetrieveAsync`. Each seam is a `protected virtual` method whose default
implementation invokes an optional delegate, so a derived connector overrides the method and a
host wiring one up assigns the delegate:

| Seam | Method | Delegate |
|---|---|---|
| Coerce one property's type | `ResolveValue` | `ValueHandlers["Modified Date"]`, `AddValueHandler(...)` |
| Coerce every property | `ResolveValue` | `ValueHandler` |
| Reshape or drop a record | `TransformEntry` | `EntryTransform` (return `null` to drop) |
| Decide the state | `ResolveState` | `StateSelector` |
| Decide the identity | `ResolveIdentity` | `IdentitySelector` |
| Read a non-JSON body | `ReadContent`, `ReadsContent` | `ContentReader` |
| Resolve an endpoint at run time | `ResolveEndPointAsync` | `EndPointResolver` |

On the builder: `CreateProvider`, `CreateAuthenticationProvider` (or the `AuthenticationFactory`
delegate), `CreateTransport`, `CreateRequestOptions`, `ConfigureProvider`.

```csharp
// Per-property type handling, registered from the derived provider's constructor.
AddValueHandler("Modified Date", value => value is string text
    ? DateTime.Parse(text, CultureInfo.InvariantCulture)
    : value);
```

Authentication beyond the built-in modes returns any Kiota `IAuthenticationProvider`; the same
request pipeline applies it. The pipeline itself is Kiota's — retry honouring `Retry-After`,
redirect, and parameter-name decoding that restores the `$` of an OData system query option —
so no connector deriving from this one writes a retry loop.

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
