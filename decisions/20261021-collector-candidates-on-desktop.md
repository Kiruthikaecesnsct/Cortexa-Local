# Collector candidates identified on the desktop

- Date: 2026-10-21
- Made by: US153 / US154 (hand-authored, approved by Alpha)
- Status: active

## Decision

The desktop collector identifies invention candidates itself. Each selected document is read whole and sent to Gemini in its own request, and the candidates are shown locally in Review. Candidates are not uploaded to Cortexa, and the existing unit knowledge extraction is not replaced.

## Why

This changes the earlier plan, where the desktop extracted knowledge only and Cortexa found candidates. Alpha chose local identification so the candidate pass works without a server round trip. The main alternative rejected was chunked extraction, because it can split an invention across chunks.

## Affects

- `desktop/collector` only. `services/`, `contracts/` and `frontend/` are not changed by this decision.
- Any future work that wants candidates on the server must write a new decision first. Uploading candidates is out of scope for US153 and US154.
- Token limit enforcement uses `Ai:Gemini:MaxInputTokens`. A file over the limit fails and is never truncated.
