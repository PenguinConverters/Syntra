# Contributing to Syntra

Thanks for your interest in Syntra. This document covers how to build the project, the
coding standards we enforce, and how to get a change merged.

By contributing you agree that your contributions are licensed under the
[Apache License 2.0](../LICENSE).

## Before You Start

- **Security issue?** Do not open an issue. Follow [SECURITY.md](SECURITY.md).
- **Large change or new connector?** Open an issue first to agree on the approach.
  Unsolicited large pull requests are hard to review and may be declined.
- **Bug fix or small improvement?** Go straight to a pull request.

## Building

### Prerequisites

- .NET 8.0 SDK
- SQL Server 2016+ (only for the SQL schema projects and `Consumer.AzureSQL`)

### The Keyra SDK dependency

`PenguinConverters.Syntra.Core` depends on the **Keyra SDK**
(`PenguinConverters.Keyra`) for credential protection. It supplies the `Secret` configuration
node, the `Decryptor` that opens it, and `DecryptorBuilder`. `PenguinConverters.Keyra.Core` and
`PenguinConverters.CandyStore` come with it transitively; CandyStore carries the native engine as
per-RID runtime assets, so nothing needs installing separately.

Keyra is **proprietary and separately licensed** — it is not covered by Syntra's Apache-2.0
license and its source is not public — but it is published on nuget.org, so `git clone` and
`dotnet build` work with no feed configuration and no credentials. Shipping a product that uses
it still requires a Keyra licence; see [NOTICE](../NOTICE).

Key storage providers are discovered at runtime, not referenced at build time. A portable
password-protected share (`aes-gcm`) needs nothing extra. A Windows-identity key additionally
needs `PenguinConverters.Keyra.KeyStorageProvider.DpapiNg` deployed beside the host — it targets
`net8.0-windows`, which is why no Syntra project references it.

### Protecting a credential

Configuration carries credentials as `PenguinConverters.Keyra.Settings.Secret`, never as a plain
string. A protected value serializes as `{ "Value": "<ciphertext>", "Protected": true }`; an
unprotected one holds its plaintext and states `Protected: false`.

Produce the ciphertext once, with the key that will open it:

```csharp
using Decryptor decryptor = new DecryptorBuilder()
    .UseKeyFile(@"D:\secure\syntra.keyra")
    .WithPassword(password)
    .Build();

Secret connectionString = Secret.Protect(decryptor, "Server=…;Database=…");
```

Point a configuration at the key with a `keyra:` section — `keyFile`, or `shareVariable` naming an
environment variable that holds an armored share. The key password is never read from the
configuration file, since that file is what the key protects; it comes from
`SYNTRA_KEYRA_PASSWORD`, or from the variable `passwordVariable` names.

### Build and test

Each component is an independent solution:

```bash
dotnet build PenguinConverters.Syntra.Core/PenguinConverters.Syntra.Core.sln
dotnet test  PenguinConverters.Syntra.Core.Tests/
```

Build everything:

```bash
for sln in $(git ls-files '*.sln'); do dotnet build "$sln" -c Release; done
```

## Coding Standards

These are enforced in review. Please read
[docs/naming-conventions.md](../docs/naming-conventions.md) — it is the golden standard.

- **Root namespace is `PenguinConverters.Syntra.*`.** No other root is permitted.
- **Never use `var`.** Always write the explicit type: `string name = "value";`
- **Never put secrets in plain strings.** Use `PenguinConverters.Keyra.SecureBuffer<T>`.
- Prefer `IAsyncEnumerable<T>` for streaming large datasets.
- Use `ConcurrentDictionary` for thread-safe collections in parallel processing.
- Log through `Microsoft.Extensions.Logging.ILogger` — never `Console.WriteLine`.
- Configuration is JSON (API/Functions) or YAML (Service/Console).
- Public APIs carry XML documentation comments.
- Target framework is `net8.0`.

### Database objects

- `S1` prefix for all Syntra tables; `S1FE` for frontend stored procedures.
- Primary key `{Table}Id` (UNIQUEIDENTIFIER), identity `{Table}Identity` (INT IDENTITY).
- Audit columns: `{Table}Inserted`, `{Table}InsertedBy`, `{Table}Updated`,
  `{Table}UpdatedBy`, `{Table}Deleted`, `{Table}RowVersion`.
- Shared schema targets SQL 2016+ (DSP Sql130). Azure-only objects belong in
  `Consumer.AzureSQL.Schema` (DSP Sql160).

### Tests

- **NUnit** — not xUnit, not MSTest.
- Test project at repository root: `{ProjectName}.Tests/`
- File naming: `{ClassName}Tests.cs`
- Arrange/Act/Assert, marked with `//Arrange`, `//Act`, `//Assert` comments
- Use `[SetUp]`, `[Test]`, `[TestCase]`

```csharp
[Test]
public void Build_WithValidBaseDn_ReturnsConnection()
{
    //Arrange
    ConnectionBuilder builder = new ConnectionBuilder();
    string baseDn = "DC=contoso,DC=com";

    //Act
    IConnection connection = builder.AddBaseDN(baseDn).Build();

    //Assert
    Assert.That(connection, Is.Not.Null);
}
```

Use `contoso.com` / `example.com` placeholders in tests and docs. **Never** commit real
hostnames, domain names, IP addresses, distinguished names, tenant IDs, or company names.

## Adding a Connector

1. Create a solution folder at the repository root:
   `PenguinConverters.Syntra.Provider.{Name}/` (or `Consumer.{Name}` for a destination).
2. Create the project folder inside it with a matching `.csproj`, plus a `.sln` at the
   solution folder level. No `src/` or `tests/` wrapper folders.
3. Implement `IProviderBuilder` / `IConsumerBuilder`.
4. Implement a configuration class with Keyra-protected credential fields.
5. Add a test project `PenguinConverters.Syntra.Provider.{Name}.Tests/` at the root.
6. Document the connector in the README table and in
   [docs/connector-development.md](../docs/connector-development.md).

Connectors are loaded dynamically by reflection — no changes to Core are needed.

## Pull Requests

1. Fork the repository and create a branch from `main`.
2. Keep the change focused. One logical change per pull request.
3. Add or update tests. New behaviour without tests will be asked for changes.
4. Update documentation affected by your change.
5. Make sure CI passes and all review conversations are resolved.
6. A code owner must approve before merge. Pull requests are **squash-merged**.

### Commit messages

```
1.0.0.1
ADD
Short description of what changed and why
```

Line 1 is the version, line 2 is the action in capitals (`ADD`, `CHANGE`, `FIX`,
`REMOVE`), line 3 onward is the detail.

Do not add `Co-Authored-By` trailers for automated tooling.

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). By participating you
are expected to uphold it.

## Questions

Open a [discussion](https://github.com/PenguinConverters/Syntra/discussions) or an issue.
