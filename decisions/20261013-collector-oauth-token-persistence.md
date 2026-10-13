# Collector OAuth Token Persistence

**Date**: 2026-10-13
**Item**: US151 (GitHub device flow and App sign-in), US152 (Entra ID sign-in for Azure DevOps)
**Status**: Accepted by Alpha. Approved exception to US138 for OAuth tokens only. Not yet implemented.

## Decision

- OAuth tokens may be persisted, encrypted, in Windows Credential Manager so sign-in survives a restart.
  - US151: GitHub device flow and GitHub App user-to-server tokens (access token and rotating refresh token pair).
  - US152: the encrypted MSAL token cache for Entra ID sign-in to Azure DevOps.
- This is an exception to the US138 rule for OAuth tokens only. It does not change the rule for anything else.
- Remote PATs and the SSH passphrase stay in session memory only. They are never written to Credential Manager or to disk.
- The US138 startup cleanup must not delete the OAuth entries. It keeps purging the old saved PAT and SSH passphrase slots.
- Sign-out clears the OAuth entries from Credential Manager.
- MSAL cache bytes and App tokens are written only through `CredentialManagerStore`, never to settings files or logs.

## Context

US138 kept remote credentials out of Credential Manager because a long-lived secret on disk was the risk. OAuth tokens from the device flow, the GitHub App, and Entra ID are short-lived or rotating and tied to a user grant. Requiring a fresh sign-in after every restart was judged a worse trade for these two flows. Persistence is limited to the encrypted store, and the token scope is limited to the collector's own app registrations.

## Consequences

- `CredentialManagerStore` gets a separate slot family for OAuth tokens. The US138 purge must match only the PAT and SSH passphrase slots.
- The US138 rule (session-only remote credentials, recorded in `20261008-collector-remote-pat-handling.md`) is superseded for OAuth tokens only. Its PAT and SSH rules still hold.
- The US151 and US152 capsules no longer block on the storage question. Their external prerequisites (app registrations, admin approval, client IDs) are unchanged.
- Open: the exact slot names and the refresh-token rotation write path are left to implementation.
