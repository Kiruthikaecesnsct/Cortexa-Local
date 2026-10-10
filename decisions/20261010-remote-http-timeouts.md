# Remote HTTP Timeouts and Retry Limits

**Date**: 2026-10-10
**Item**: US141 (Azure DevOps: pick files before splitting)
**Status**: Merged to dev. Applies to the shared GitHub and Azure DevOps HTTP path.

## Decision

- Remote HTTP clients use an infinite `HttpClient.Timeout`. A total client timeout would cut off rate-limit waits and large blob reads.
- `RateLimitHandler` applies a per-attempt timeout (`TimeoutSeconds`) around the send and the body buffering only. Rate-limit waits follow only the caller's cancel token.
- Blob reads in `GuardedReadStream` are bounded by an idle timeout. A stalled read fails. A slow read that keeps moving continues.
- The retry limit is set per provider through `MaxRateLimitRetries`. Azure DevOps uses 10.

## Consequences

- Do not set a total `HttpClient.Timeout` on GitHub or Azure DevOps clients.
- A deadline hit is a failure of one attempt. The caller's cancel token still stops the whole operation.
- A new remote provider sets its own `MaxRateLimitRetries`. Do not share one value across providers.
