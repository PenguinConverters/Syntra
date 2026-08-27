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

Each archive is **self-contained**, so a target server needs no .NET runtime installed. Neither
single-file nor trimmed: connectors are resolved by assembly name at runtime, and both of those
transformations break reflection-based loading.

The hosts reference only `Core`. Every connector is loaded by name, so nothing pulls them into the
publish output automatically - the workflow publishes each connector into the same folder
deliberately.

> **Known gap.** `InstanceBuilder` resolves a connector with `Assembly.Load`, which reads the
> application's `deps.json` and does not probe the application directory. A connector sitting beside
> the host is therefore **not** found at runtime. The archives carry the connectors, but loading
> them needs a resolver change in `Core` - an `AssemblyDependencyResolver`, a `Resolving` handler,
> or `Assembly.LoadFrom` against a known directory. Until that lands, a release is packaged
> correctly but a deployed host will not find its connectors.

## Who can release

The draft is inert: it is not published, and nobody can download it. Producing one is therefore
not the sensitive act - promoting it is, and that happens on-premises behind the release group's
approval. See the release signing plan for the authorization model.
