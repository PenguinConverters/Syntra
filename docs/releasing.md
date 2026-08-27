# Syntra - Releasing

A release is staged by GitHub and finished on-premises. GitHub builds, tests and packages, then
opens a **draft** release carrying unsigned artifacts. The on-prem runner verifies them, signs the
Windows binaries with the EV certificate, replaces the unsigned copies and promotes the draft.

Only the second half is implemented by this repository. This document covers what GitHub does.

## Versioning

The version has one source of truth: **the tag**.

| Property | Value | Why |
|---|---|---|
| `Version` | the tag, e.g. `1.0.0.26` | Passed as `-p:Version=` by CI, which overrides the fallback in `Directory.Build.props` |
| `FileVersion` | `$(Version)` | What a support call reads off the file properties dialog |
| `InformationalVersion` | `$(Version)+<commit>` | The SDK appends the commit, so a binary names the source it came from |
| `AssemblyVersion` | **pinned at `1.0.0.0`** | The identity half of a strong name. Moving it per release breaks every binding against these assemblies |

`AssemblyVersion` changes only on a deliberate major version, and doing so is a breaking change for
anything that references these assemblies.

The fallback in `Directory.Build.props` exists so a local build produces something sane. A release
never reads its version out of a file somebody forgot to bump.

## Cutting a release

Tag a commit that is already on `main`:

```
git tag 1.0.0.26
git push origin 1.0.0.26
```

The tag format is `MAJOR.MINOR.PATCH.BUILD`, matching the version carried in commit subjects. A
leading `v` is accepted and stripped.

To stage a release without creating a tag - useful for exercising the pipeline - run the **Release**
workflow manually and give it a version. It produces a draft the same way; GitHub creates the tag
only if the draft is published.

## What the workflow refuses

Everything that could collide with an existing release is decided before anything is built:

- **A malformed version.** Not `MAJOR.MINOR.PATCH.BUILD`, or a field above 65535, which a Windows
  file version resource cannot hold - it would truncate silently in the binary.
- **A tag outside `main`.** A release carries the company signature, so it comes from reviewed
  history.
- **A version that is already published.** Replacing the assets of a published release changes the
  bytes behind a name somebody may already have downloaded. Cut a new version instead.

Going backwards warns rather than blocks, so re-releasing an older line does not need a workflow
change.

An existing **draft** for the same version is reused and its assets replaced, so a re-run after a
failed upload repairs the draft instead of erroring or leaving duplicates behind.

## What lands on the draft

Two archives per runtime identifier - the console host and the service host - plus `SHA256SUMS.txt`:

```
syntra-console-1.0.0.26-win-x64.zip
syntra-service-1.0.0.26-win-x64.zip
syntra-console-1.0.0.26-linux-x64.tar.gz
syntra-service-1.0.0.26-linux-x64.tar.gz
SHA256SUMS.txt
```

Each archive is **self-contained**, so a target server needs no .NET runtime installed. That is
set in the host projects themselves - conditioned on a runtime identifier being given, because a
build without one cannot produce a self-contained app - so a hand-run `dotnet publish -r win-x64`
produces the same thing CI does.

Nothing is ever **trimmed**. Connectors are resolved by name through reflection, which the trimmer
cannot see; it would remove them and the failure would surface only at run time on a customer's
server.

## How connectors reach the host

A connector can be deployed two ways, and both work.

**Referenced.** Every connector that ships in the box is a project reference of both hosts, which
puts it in the application's `deps.json` and lets the default load context resolve it.

**Dropped in as a file.** Put the assembly in `connectors/` beside the host, or beside the host
itself, and it is found by name. This is how a connector built after the host was released - or one
built by somebody else - is deployed without rebuilding anything.

The file path exists because .NET does not probe the application directory. The .NET Framework
loader did, which is why dropping an assembly next to the host used to be all that was required;
`InstanceBuilder` now performs that probe itself.

A file is only loaded if it carries the **same strong name as `Core`**. The identity is read from
the file's manifest before anything is loaded, so an assembly signed with another key never enters
the process. `WithPublicKey` replaces that expectation when a deployment trusts a different key.

> A strong name is an identity, not a proof of authorship: .NET does not verify strong-name
> signatures when it loads an assembly, so this establishes *which* assembly claims to be which,
> not *who* produced it. Authorship is what Authenticode establishes, and that is verified when a
> release is signed rather than when a connector is loaded.

Either way the reference decides only what is *deployed*. `InstanceBuilder` still resolves a
connector by name from configuration, so a deployment enables only the connectors its configuration
names.

## Who can release

The draft is inert: it is not published, and nobody can download it. Producing one is therefore
not the sensitive act - promoting it is, and that happens on-premises behind the release group's
approval. See the release signing plan for the authorization model.
