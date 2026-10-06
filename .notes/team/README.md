# Team memory

Shared context for Claude Code sessions working on this project from different machines at the same time. It answers two questions the work-item JSON cannot: why a story was created (the analysis behind it), and who is doing what right now.

## Where this lives

Team memory lives on a dedicated `team-notes` branch, not on any feature branch. Feature branches never carry these files. Each session reaches the branch through a side checkout (worktree) at `.team/`, so a dev on `develop/US042` and a dev on `develop/US050` both see the same team state after a sync, without merging code.

Sync is fetch, reconcile, push, scoped to this folder only. Every op pulls first. It runs at milestones (item start, plan ready, PR open, review, merge), on session start, and on a light heartbeat between them.

## Derived vs source

Two kinds of file live here, and mixing them was the original bug.

**Source** - claim cards, capsules, contracts, decisions, the briefing. Committed. One file per thing, one owner each.

**Derived** - `ACTIVE.md`. Built locally from the claim cards on every op and **never committed**. It cannot go stale between milestones and it cannot conflict, because no two machines ever push it.

The active-work table used to sit inside `TEAM_BRIEFING.md`. A briefing is only rewritten at item boundaries, so it read "No active sessions" for hours while three machines were mid-item. Live state has to be derived, not written.

## What loads into a session

Only three things ever load into an agent's context:

1. `ACTIVE.md` - who is working on what right now. Injected on session start.
2. `TEAM_BRIEFING.md` - the capped narrative digest.
3. The active item's capsule - `context/{ITEM_ID}.md`.

Everything else (other capsules, raw claim cards, decisions, contracts) is read on demand, only when a task references it. The injected size stays fixed no matter how large the project grows.

## Machine identity

Claim cards are keyed `{ITEM_ID}.{machine}.json`. The id resolves from `TEAM_MACHINE`, then `~/.claude/machine-id`, then `COMPUTERNAME`, then `hostname`, normalized to lowercase with the domain stripped.

In Docker the default hostname is the container id, so it changes on every recreate and old claims turn into orphans. Set a stable id once per machine:

```bash
echo my-dev-box > ~/.claude/machine-id
```

`/team-sync sync` warns when it sees an unstable id.

## Token caps (enforced on regenerate, not optional)

- `TEAM_BRIEFING.md` - hard cap about 1500 tokens. Rewritten on `brief`, never appended, so it cannot grow. Over budget means compress the oldest and least active sections first. Narrative only - no claims table.
- `context/{ITEM_ID}.md` - about 400 to 600 tokens. Terse. Written once at creation.
- `ACTIVE.md` - bounded by the number of live claims. Derived, so it shrinks on its own when claims are released.
- Claim cards - tiny JSON, written only by `team-card.sh`. Never hand-written and never injected raw.

## Claim status values

`planning` > `implementing` > `review` > `reviewing`, plus `blocked` at any point.

`review` means the author is waiting on CI or a reviewer. `reviewing` means someone is actively reviewing it. The distinction matters because a PR can sit in `review` for a day, and without it the card would still read `implementing`.

## Lifecycle - cleared when it is time

- Claim card - released to `archive/` only when the item is merged. An unmerged item keeps its card so the other machines know it is still in flight. A claim with no heartbeat for over 2 hours is stale and drops out of the live view. Each machine archives its own stale cards on sync; someone else's is archived only on an explicit `/team-sync prune`, because a sleeping laptop is not a quit session.
- Capsule - archived out of `context/` once the item is Done and merged. It stops loading. Git history keeps it.
- Decision - the briefing shows only active and recent ones. Once implemented and old, it folds to a one-liner or moves to `archive/`. Superseded decisions are dropped.
- Contract - dropped when its item merges. After that the real code is the source of truth. Only in-flight contracts stay.

The active working set stays roughly constant. Everything retired lives in git history, out of context.

## Owner

`project-manager-expert` owns this folder, the way a real project manager runs the board: writes capsules at creation, regenerates the briefing, records decisions, flags overlapping claims, and prunes stale data. No other agent writes here.

## Layout

```
ACTIVE.md                   live claim view - GENERATED, gitignored, never committed
TEAM_BRIEFING.md            capped narrative digest
Claim_Card_Template.json    reference shape only. Kept out of registry/ on purpose
context/{ITEM_ID}.md        analysis capsule per work item
registry/{ITEM_ID}.{machine}.json   live claim card per active session
contracts/{name}.md         API or DTO shapes dependents build against
decisions/{YYYYMMDD}-{slug}.md      cross-cutting decisions, one per file
archive/                    released claims and closed capsules
.gitignore                  keeps ACTIVE.md and .local/ off the branch
.gitattributes              `* -text`, so Windows and Linux see identical bytes
.local/                     sync log, last error, change fingerprints. Local only.
```

## Conflicts

Two machines pushing at once is normal. `team-sync` handles it:

- Rebase with autostash on every pull.
- A conflicting claim card resolves by filename - the machine named in the file wins its own card.
- `ACTIVE.md` and `TEAM_BRIEFING.md` resolve to the local copy, then get regenerated.
- Anything else aborts the rebase and reports. Nothing is guessed at.
- Push retries up to 3 times with a jittered backoff, pulling between tries.

## Rules

- Never put secrets, tokens, or PII in any team file.
- Plain WritingStyle prose. No em-dashes, no buzzwords, no emoji in prose.
- One file per thing. Never a shared blob that everyone edits.
- The briefing is derived from the capsules and decisions. On a merge conflict, discard it and regenerate from the small files. Never hand-merge it.
- Never commit `ACTIVE.md` or `.local/`.
- Claim cards are written by `team-card.sh`, never by hand. Hand-written cards drifted - empty `started`, invented timestamps, one machine spelled two ways.
- Nothing but real claim cards goes in `registry/`. A template sitting there is read as a live claim, shows up as a permanently stale one, and gets archived by the next prune.
