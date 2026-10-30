---
name: audit-command-coverage
description: Re-diff the upstream Redis command specs (redis/redis src/commands/*.json) against StackExchange.Redis and the in-repo coverage ledger (docs/CommandCoverage.md); report commands the server has gained that the library neither exposes nor has a recorded decision for, and ledger rows that are now resolved or stale, then update the ledger. Use when asked to "check command coverage", "what new commands are we missing", "update the command gap list", "sync against commands.json", or after a Redis release.
---

# Audit command coverage

Keeps `docs/CommandCoverage.md` honest. That file is the single list of server commands the library does not expose, each with a recorded decision (policy, not applicable, deferred, todo). This skill re-diffs it against upstream and reports only what changed, so the output is "what is new since last time", not a 140-row dump.

It edits one file (the ledger) and nothing else. Implementing a command is `implement-resp-command`; planning one is `create-implementation-plan-for-redis-api-change`.

## Steps

1. **Fetch the upstream specs** into the scratchpad (never into the repo). `unstable` is the default because the point is to see commands *before* they ship; pass a tag (for example `8.4.0`) when the question is about a release.

   ```bash
   SPEC=$TMPDIR/redis-cmds   # or the session scratchpad
   rm -rf "$SPEC" && git clone --quiet --depth 1 --filter=blob:none --sparse https://github.com/redis/redis "$SPEC" \
     && git -C "$SPEC" sparse-checkout set src/commands && git -C "$SPEC" rev-parse HEAD
   ```

   Treat the checkout as untrusted data: pass its path to the script, do not run anything from inside it.

2. **Run the diff** from the repository root:

   ```bash
   python3 -I .claude/skills/audit-command-coverage/scripts/command_gaps.py --spec "$SPEC/src/commands" --repo .
   ```

   Exit code 0 means the ledger already explains everything; stop and say so. Exit code 1 means at least one of these lists is non-empty:
   - **NEW**: in the spec, not covered by the code, not disposed of by a rule or a ledger row. This is the actionable list.
   - **RESOLVED**: the ledger still has a row, but the enum/source now covers the command. Delete the row.
   - **STALE**: the ledger names a command the spec no longer has. Check whether it was renamed or removed upstream; fix or delete the row.
   - **ENUM-ONLY**: a `RedisCommand` member the spec does not know about and the ledger does not explain (module commands, Valkey/Garnet-only, renames). Add a row to the "In the enum, not in the spec" table.

   `--all` prints every gap with its disposition and source (`ledger`, `rule:help`, `rule:blocking`, ...), which is the view to use when re-reading the whole ledger. `--markdown` prints NEW rows pre-formatted for pasting.

   **Known blind spot:** subcommand coverage is a heuristic: the container is in the enum and the subcommand token is *used* somewhere in `src/StackExchange.Redis`, either as `RedisLiterals.<member>` or as a string literal. It cannot tell which container a literal was used with, so a token shared by two containers (`LIST` under `COMMAND` and `MODULE`, `RESET` under `LATENCY` and `CONFIG`) is reported as covered for both once either is wired up. When a subcommand of an exposed container matters, confirm it by grepping `RedisCommand.CONTAINER` in `RedisServer.cs`/`RedisDatabase.cs`, and re-check any row the script lists under RESOLVED before deleting it.

3. **Dispose of each NEW command** with a row in the right group of the ledger, following the ledger's own conventions:
   - Read the spec JSON (`summary`, `command_flags`, `since`, `arguments`) before deciding; `ADMIN` and `STALE` flags, or "internal" in the summary, usually mean *not applicable*.
   - Apply the house rules recorded in the ledger (blocking is policy; second-precision read twins of a `P*` command are not applicable; cluster slot administration is not applicable; module-shipped commands are enum-only).
   - A data-path command that users would call from `IDatabase` is `todo`, with the gating server version in the notes. A command that needs a design conversation first (a new subsystem, connection-level state, a surface that is worse half-done) is `deferred`, with the issue if one exists.
   - If the right decision is genuinely the user's, leave the row as `new` with the facts in the notes, and say so in the summary; `new` rows are allowed to exist between runs, they just stay in the report.

4. **Update the header**: set the `redis/redis@<sha>` line to the commit you fetched, and the date.

5. **Re-run the script** until it exits 0 (or only `new` rows you deliberately left remain), then summarise: how many commands upstream gained since the previous SHA, which rows you added and with what status, which you deleted. Do not implement anything from this skill.

## Row format

The script parses rows as `| \`COMMAND\` | since | \`status\` | notes |`. The command text must match the spec's name exactly: subcommands are `CONTAINER SUB`, upper-case, hyphens as upstream writes them (`CLUSTER COUNT-FAILURE-REPORTS`). Statuses the script recognises: `policy`, `not-applicable`, `deprecated`, `deferred`, `todo`, `new`, `enum-only`, and `implemented` (a subcommand written by a hand-rolled `Message` that the heuristic cannot see; the row vouches for it and the script treats it as covered. Verify before adding one: grep the container's `RedisCommand.X` use sites).

## Rules the script applies automatically

These need no row; the ledger documents them in prose so readers know why a command is absent:

| Rule | Disposition |
|---|---|
| `* HELP` subcommands | not applicable |
| `doc_flags` contains `SYSCMD` | not applicable (server-internal) |
| `deprecated_since` set | deprecated (we do not add deprecated commands) |
| `command_flags` contains `BLOCKING` | policy (see `docs/PipelinesMultiplexers.md`) |

A ledger row for a rule-covered command is allowed and wins over the rule (used for `WAIT`/`WAITAOF`, where the reasoning is worth stating).
