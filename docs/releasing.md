# Syntra - Releasing

Releases are built and signed **on-premises**. GitHub holds the source and runs
pull-request CI; it produces nothing that ships.

The reason is the signing key: an EV certificate held in hardware that does not leave the
machine it is plugged into. A release cannot be produced by someone who only has commit
access - it needs that hardware.

This document covers what a release is and what you get when you download one.

## Versioning

The version has one source of truth: **the tag**.

| Property | Value | Why |
|---|---|---|
| `Version` | the tag, e.g. `1.0.0.34` | Passed as `-p:Version=` by CI, which overrides the fallback in `Directory.Build.props` |
| `FileVersion` | `$(Version)` | What a support call reads off the file properties dialog |
| `InformationalVersion` | `$(Version)+<commit>` | The SDK appends the commit, so a binary names the source it came from |
| `AssemblyVersion` | **pinned at `1.0.0.0`** | The identity half of a strong name. Moving it per release breaks every binding against these assemblies |

`AssemblyVersion` changes only on a deliberate major version, and doing so is a breaking
change for anything that references these assemblies.

The fallback in `Directory.Build.props` exists so a local build produces something sane. A
release never reads its version out of a file somebody forgot to bump.

## Cutting a release

Tag a commit that is already on `main`, and push the tag:

```
git tag 1.0.0.34
git push origin 1.0.0.34
```

The tag format is `MAJOR.MINOR.PATCH.BUILD`, matching the version carried in commit
subjects. The tag is picked up on its own; a release then appears once it has been built,
signed and approved.

A tag produces no release at all if it is malformed, if any field is above 65535 - which a
Windows file version resource cannot hold, and would truncate silently in the binary - or
if it points at a commit that is not contained in `main`. A release carries the company
signature, so it is built from history that has been through review.

A version that is already published is never rebuilt: replacing its assets would change the
bytes behind a name somebody may already have downloaded. Cut a new version instead.

## What a release contains

Two archives per runtime identifier - the console host and the service host - plus
`SHA256SUMS.txt`:

```
syntra-console-1.0.0.34-win-x64.zip        signed
syntra-service-1.0.0.34-win-x64.zip        signed
syntra-console-1.0.0.34-linux-x64.tar.gz
syntra-service-1.0.0.34-linux-x64.tar.gz
SHA256SUMS.txt
```

Inside an archive, at its root:

- **The executable.** `CMDSYNTRA.exe` in a console archive, `svcsyntra.exe` in a service
  one, and the same names without the extension on Linux. Everything beside it is a library
  it loads. The `.dll` of the same name is the application itself and the `.exe` is the
  native launcher that starts it, so the two belong together and neither runs alone.
- **`README.txt`**, naming that executable and the command that runs it, saying where
  configuration goes, and how to check what was downloaded.
- **`Configuration/`**, where your `.yaml`, `.yml` or `.json` files belong. The service host
  loads every file it finds there when it starts, and each needs a cron schedule of its own;
  the console host runs the single file you name on the command line.

No archive carries `.pdb` or `.xml` files. Debug symbols and API documentation are build
output rather than product, and a deployed host has no use for either.

Each archive is **self-contained**, so a target server needs no .NET runtime installed.
That is set in the host projects themselves - conditioned on a runtime identifier being
given, because a build without one cannot produce a self-contained app - so a hand-run
`dotnet publish -r win-x64` produces the same thing CI does.

Nothing is ever **trimmed**. Connectors are resolved by name through reflection, which the
trimmer cannot see; it would remove them and the failure would surface only at run time on
a customer's server.

## Verifying a download

Every asset is covered by `SHA256SUMS.txt`:

```
sha256sum -c SHA256SUMS.txt
```

In the **Windows** archives, everything Syntra built is Authenticode-signed with the
Penguin Converters certificate, as is any dependency that arrived unsigned. Third-party
signatures are left exactly as their publishers set them: re-signing somebody else's binary
with our certificate would destroy the provenance of a file we did not build.

```powershell
Get-AuthenticodeSignature .\CMDSYNTRA.exe | Format-List Status, SignerCertificate
```

The **Linux** archives are not signed. Authenticode is a Windows facility, and these
archives are packed on Linux and never rewritten - repacking one on Windows would drop the
execute bit off the apphost. Check them against `SHA256SUMS.txt`.

## How connectors reach the host

A connector can be deployed two ways, and both work.

**Referenced.** Every connector that ships in the box is a project reference of both hosts,
which puts it in the application's `deps.json` and lets the default load context resolve
it.

**Dropped in as a file.** Put the assembly in `connectors/` beside the host, or beside the
host itself, and it is found by name. This is how a connector built after the host was
released - or one built by somebody else - is deployed without rebuilding anything.

The file path exists because .NET does not probe the application directory. The .NET
Framework loader did, which is why dropping an assembly next to the host used to be all
that was required; `InstanceBuilder` now performs that probe itself.

A file is only loaded if it carries the **same strong name as `Core`**. The identity is
read from the file's manifest before anything is loaded, so an assembly signed with another
key never enters the process. `WithPublicKey` replaces that expectation when a deployment
trusts a different key.

That same public key decides which files are signed when a release is built, so the
identity checked at load time and the identity signed at release time are one thing.

A strong name is an identity, not a proof of authorship: .NET does not verify strong-name
signatures when it loads an assembly, so it establishes *which* assembly claims to be
which, not *who* produced it.

Authorship is what Authenticode establishes, and the loader checks it. The publisher of a
connector file is verified against the publisher of `Core` itself, and the result is
**logged, never enforced**:

| Result | Logged as |
|---|---|
| Signed by the expected publisher | information |
| Signed by somebody else | **warning**, and loaded |
| Not signed at all | **warning**, and loaded |
| Signature does not verify | **warning**, and loaded |
| Not on Windows | debug - Authenticode is a Windows facility |

Nothing is refused on this basis. An in-house build, or a connector taken from a branch
during an investigation, is a legitimate thing to run; the loader records what was run
rather than deciding it. `WithPublisher` replaces the expected publisher where a
deployment trusts a different one.

Either way the reference decides only what is *deployed*. `InstanceBuilder` still resolves
a connector by name from configuration, so a deployment enables only the connectors its
configuration names.
