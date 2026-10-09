# Collector Remote PAT Handling

**Date**: 2026-10-08
**Item**: US134 (GitHub and Azure DevOps collectors)
**Status**: Merged to dev (PR #14). Superseded in part by US138 (PR #19, 2026-10-09).

## Decision

- Superseded by US138: remote PATs and the SSH passphrase are held in session memory only. They are never written to Windows Credential Manager or to disk. Previously saved GitHub, Azure DevOps, and SSH passphrase secrets are deleted at startup. The SSH host-key fingerprint pin is removed (accepted risk).
- Original US134 rule, now superseded: GitHub and Azure DevOps personal access tokens were stored in Windows Credential Manager (slots `SecretSlot GitHubPat` and `SecretSlot AzureDevOpsPat`).
- A per-client HTTP handler attaches the token only to the configured API origin. Redirects are disabled, so a redirect cannot send the token to another host.
- A per-provider rate-limit gate pauses requests near the limit. A 429 response is retried after the wait.
- Pinned API versions:
  - GitHub: `X-GitHub-Api-Version: 2026-03-10`
  - Azure DevOps: `api-version=7.1`

## Context

A token sent to the wrong host leaks the credential. Disabling redirects and scoping the header to one origin closes that path. Storing the token in Credential Manager keeps it out of settings files and logs.

## Consequences

- Changing an API version is a code change in the handler, not a config change. Re-check the pinned version against the provider release notes before bumping it.
- The SSH collector (US135) should use the same credential-store pattern for its key material, not a new store.
- Open: the Azure DevOps UI, the rate-limit banner, and Esc cancel were not verified live in US134.
