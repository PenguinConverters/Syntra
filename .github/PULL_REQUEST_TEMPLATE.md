<!--
  Thanks for contributing to Syntra.

  SECURITY: Do not use a pull request to report a vulnerability.
  See .github/SECURITY.md for private reporting.
-->

## Summary

<!-- What does this change do, and why? -->

## Related Issue

<!-- e.g. Closes #123. For anything non-trivial, an issue should exist first. -->

## Type of Change

- [ ] `FIX` — bug fix (non-breaking)
- [ ] `ADD` — new feature (non-breaking)
- [ ] `CHANGE` — change to existing behaviour
- [ ] `REMOVE` — removal of a feature or API
- [ ] Breaking change (existing configurations or callers must be updated)
- [ ] Documentation only

## Components Touched

- [ ] Core
- [ ] Provider (which: ______)
- [ ] Consumer (which: ______)
- [ ] Api
- [ ] Host (Console / Service)
- [ ] SQL schema (SharedSchema / AzureSQL.Schema)
- [ ] Build / CI / docs

## Checklist

- [ ] Namespaces are rooted at `PenguinConverters.Syntra.*`
- [ ] No `var` — all types written explicitly
- [ ] No secrets in plain strings; sensitive values use `SecureBuffer<T>`
- [ ] **No real hostnames, domains, IPs, DNs, tenant IDs, or company names** — placeholders only
- [ ] NUnit tests added or updated, following `//Arrange` `//Act` `//Assert`
- [ ] `dotnet build` and `dotnet test` pass locally
- [ ] Documentation updated (README / ARCHITECTURE / docs/)
- [ ] Database changes follow the `S1` prefix and audit-column conventions

## Database Impact

<!-- Delete if not applicable. Describe schema changes and whether they are breaking
     for existing deployments, plus any migration steps. -->

## Testing

<!-- How did you verify this? Include commands, configuration, and results. -->
