# Security Policy

Penguin Converters AG takes the security of Syntra seriously. Syntra is an Identity &
Access Management framework — a vulnerability here can affect the security posture of
every system it synchronizes. We appreciate responsible disclosure.

## Supported Versions

| Version | Supported |
|---------|-----------|
| 1.0.x   | ✅ Actively supported |
| < 1.0   | ❌ Pre-release, not supported |

Only the latest patch release of a supported minor version receives security fixes.

## Reporting a Vulnerability

**Do not open a public issue, pull request, or discussion for a security vulnerability.**

Report privately through GitHub's **[private vulnerability reporting](https://github.com/PenguinConverters/Syntra/security/advisories/new)**.
This creates a confidential advisory visible only to you and the maintainers.

If you cannot use GitHub, email **security@penguinconverters.ch** with the subject line
`[Syntra Security]`.

### What to include

- Affected component (e.g. `Provider.ActiveDirectory`, `Api`, `Consumer.AzureSQL`) and version
- Type of issue (authentication bypass, privilege escalation, injection, credential exposure, …)
- Step-by-step reproduction, including any configuration required
- Proof-of-concept, if you have one
- Impact assessment — what an attacker gains

### What to expect

| Stage | Target |
|-------|--------|
| Acknowledgement of your report | 3 business days |
| Initial assessment and severity rating | 10 business days |
| Fix or documented mitigation for critical issues | 30 days |
| Coordinated public disclosure | After a fix ships, by agreement with you |

We will keep you updated as the investigation progresses, credit you in the advisory
unless you prefer to remain anonymous, and let you review the advisory before publication.

## Scope

**In scope** — anything in this repository: the Core framework, providers, consumers,
the REST API, hosts, and the SQL schema projects.

**Out of scope**

- The **Keyra SDK**, a separately licensed proprietary component. Report Keyra issues to
  security@penguinconverters.ch, not here.
- Vulnerabilities in third-party NuGet dependencies — report upstream, though we welcome
  a heads-up so we can bump the dependency.
- Findings that require an already-compromised host or an already-privileged account.
- Missing hardening headers or configuration choices in example/documentation snippets,
  unless they lead to a concrete exploit.

## Safe Harbour

We will not pursue legal action against researchers who act in good faith, avoid privacy
violations and service disruption, only interact with systems they own or are authorised
to test, and give us reasonable time to remediate before public disclosure.

## Security Practices in This Repository

- Secret scanning and push protection are enabled — do not commit credentials.
- CodeQL analysis runs on every pull request and weekly.
- Dependabot monitors NuGet and GitHub Actions dependencies.
- All credentials in configuration must be protected via the Keyra SDK; never commit
  plaintext secrets, connection strings, key files, or certificates.
