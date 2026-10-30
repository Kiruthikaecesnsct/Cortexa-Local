# Backend settings persistence pattern: in-memory override holder

- Date: 2026-10-30
- Made by: US159 (folded mid-flight, approved by Alpha)
- Status: active

## Decision

User-editable AI provider connection settings (project id, location, credentials) are persisted in the user-settings store (desktop app's local database). A singleton state holder (`IVertexSettingsState`/`VertexSettingsState`) is seeded from the store at application startup and updated synchronously when the user saves new settings. The provider (`VertexDirectProvider`) reads the effective values from this holder per-call, falling back to static `IOptions<T>` config if the holder has no value.

## Why

US158 wired `VertexDirectProvider` to read project id and location only from static `IOptions<VertexProviderOptions>` bound at DI startup. When US159 added a Settings UI row for user-entered values, those values had no path to the provider without a persistence bridge. The alternatives were:

1. **`IOptionsMonitor` file-watch reload** - rejected. This codebase uses atomic-rename write patterns (stage to temp, rename atomically to final) for persistence. `IOptionsMonitor`'s file-watch is not reliable under these patterns and could miss updates or fail to reload.
2. **Split into a prerequisite story** - rejected by Alpha in favor of folding into US159, accepting the extra scope.
3. **In-memory holder pattern (chosen)** - simple, synchronous, testable, and free from file-system race conditions. Values are loaded once at startup, updated when the user saves, and live for the session duration.

The pattern does not persist effective values to the config file. Restarting the app reloads the database and resumes from there.

## Affects

- `desktop/collector` only. `services/`, `contracts/`, and `frontend/` are not changed by this decision.
- `Collector.Application.Ports.IVertexSettingsState` defines the interface. `Collector.Infrastructure.Secrets.VertexSettingsState` implements it.
- Added during US159 implementation.
- Only the Vertex provider reads from the holder; other providers use static config or their own persistence paths.

## Recurrence

This pattern applies to any future AI provider that needs user-editable connection settings. Copy the `IVertexSettingsState` holder shape, register it in DI with the appropriate secret store read and write points, and wire the provider to query it per-call. A new story that adds such a provider should include the holder implementation as same-layer infrastructure, not split into a prerequisite.
