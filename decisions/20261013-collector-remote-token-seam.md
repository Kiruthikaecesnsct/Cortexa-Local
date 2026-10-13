# Collector Remote Token Seam

**Date**: 2026-10-13
**Item**: US151 (GitHub device flow and App sign-in), merged via PR #27. US152 (Entra ID sign-in for Azure DevOps) depends on this.
**Status**: Merged to dev. US152 not started.

## Decision

- GitHub remote requests get their token through a provider-agnostic seam.
  - `IRemoteAuthState` records which auth method each remote provider uses.
  - `IRemoteTokenResolver` selects the matching `IRemoteTokenSource` strategy for that provider.
  - `RemoteTokenAuthHandler` sends the resolved token on the request.
- Azure DevOps still uses `PatAuthHandler` until US152.
- US152 moves Azure DevOps onto `RemoteTokenAuthHandler` and adds `AzureDevOpsEntra` to `RemoteAuthMethod`. It then deletes the duplicated origin check in `PatAuthHandler`. `RemoteOrigin.IsSame` replaces it.
- OAuth client IDs are public. They ship in `appsettings.json`. A sign-in method with no client ID is shown disabled, with a note on the connect step.

## Context

Remote auth had one path, the PAT. Device flow and GitHub App sign-in need a second token source. A single resolver keeps the choice out of each handler. A new provider adds a source strategy and a registration. The HTTP pipeline does not change.

## Consequences

- Do not add provider checks to the HTTP pipeline. Pick the token in the resolver.
- PAT stays as the fallback for GitHub and remains the only Azure DevOps path until US152 lands.
- Known external blocker: the Imaginext-Innovations-Private-Limited org returns 403 to the OAuth sign-in. An org owner must approve the OAuth App under the third-party access policy, and users must meet the org's secure 2FA rule. The code path is unaffected. Live sign-in to that org is blocked until both are done.
