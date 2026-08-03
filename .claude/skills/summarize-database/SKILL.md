---
name: summarize-database
description: Profile/summarize a live Redis (RESP) database by sampling — discover key patterns, where data lives by count and by size, and what the values actually are (counters, JSON, XML, blobs, text). Use when asked to summarize, profile, analyze, audit, or characterize a Redis/Valkey/RESP database or its keyspace, understand key naming patterns, or find where memory/data is concentrated.
---

# Summarize a database

Builds a statistical profile of a **live, often production** RESP database by sampling a fraction of the keyspace. The output answers three questions:

- **(a) Key patterns** — tokenize keys so `users:1243:orders` and `users:543:orders` collapse to `users:{id}:orders`, and report the patterns by volume.
- **(b) Where the data is** — which patterns/types hold the bulk, reported **separately by count and by size** (they often disagree: many tiny keys vs. a few huge ones).
- **(c) What the values are** — numeric counters, JSON, XML, base64/binary blobs, or plain text.

This is a **read-only, sampling** analysis. You are pointed at someone's real data; correctness and not harming the server matter more than completeness.

## Hard safety rules (do not violate)

- **Read-only only.** Never run a command that writes, deletes, expires, or reconfigures. No `FLUSHALL`/`FLUSHDB`, `DEL`, `SET`, `CONFIG SET`, `DEBUG RELOAD`, etc.
- **Never `KEYS`** (or `KEYS *`) — it blocks the server for the full scan on a large keyspace. Sample with `RANDOMKEY`; only use `SCAN` (cursored, with `COUNT`) if you deliberately need coverage, never `KEYS`.
- **Prefer a replica for the analysis load.** Run `INFO replication` to read the topology, then:
  - If connected to a **primary that has replicas** (`role:master`, `connected_slaves > 0`, with `slaveN:ip=…,port=…` lines), **suggest the user re-point at one of those replicas** — `SCAN`/`MEMORY USAGE`/value reads then land on a replica instead of the production primary.
  - If **already on a replica** (`role:slave`/`replica`), **stay there — do *not* suggest switching to its primary/master**, even though `master_host`/`master_port` are visible. Moving "up" to the primary would push the load onto production, the opposite of the goal. (A replica may lag slightly, so note the profile reflects near-current, not necessarily live, state.)
- **Never blindly read a value of unknown size.** Check the size command first; peek with `GETRANGE key 0 N`, never a bare `GET` on an unknown key.
- **Bound the expensive calls** — `MEMORY USAGE key SAMPLES <small>`, and respect the sample cap below.
- **Confirm the target before connecting** — echo back host/port/db (and whether it's a replica) so the user can stop you hitting the wrong server. Also confirm you're authorized to query this (possibly production) system at all, not just that the address is right.
- **Value contents are confidential — get explicit consent before reading them.** Key *names* are read by necessity — they're the basis of the pattern analysis, so they're treated as probative and acceptable to surface. **Values are not:** the content-classification phase (step 4) pulls real data into the agent's context, which leaves the machine (sent to the model, possibly logged) and may contain PII, secrets, or tokens. Ask before running step 4. If the user declines, produce a **structure-only** summary (steps 0–3 + 5: patterns, types, sizes, TTL) and skip value sampling. Even with consent, don't sample obviously-sensitive namespaces (keys matching `*token*`/`*secret*`/`*password*`/`*session*`) unless the user opts in for those specifically.

## Tooling

Prefer `resp-cli` (the user's tool; transparent about the wire protocol); fall back to `redis-cli` if `resp-cli` is missing or lacks a command — and say so rather than silently switching. Key facts:

- `resp-cli [cmd args] -h <host> -p <port> [-a <pass>] [-n <db>] [--tls] [-3]` runs **one** command per process. `-r N` repeats a command N times — so `resp-cli -r 200 randomkey` is a clean random sampler.
- `resp-cli` has **no** `--scan`/`--bigkeys`/`--memkeys` mode. `redis-cli` does, and supports stdin **pipelining**, which matters for the probe phase (see Performance).
- **Never put the password on the command line.** `-a <pass>` leaks via the process list (`ps`, visible to other local users), shell history, **and this transcript**. Use the auth env vars instead — `RESPCLI_AUTH` (resp-cli) / `REDISCLI_AUTH` (redis-cli) — and `--user <name>` for an ACL username. Use `--tls` for in-transit encryption; never disable certificate validation (`--insecure`/`--trust`) just to make a connection succeed.

## Server compatibility (Garnet & other non-Redis RESP servers)

The target may be a RESP-compatible server rather than stock Redis (this library explicitly supports Garnet, Valkey, etc.). **Garnet in particular advertises a Redis version via `HELLO` (e.g. `redis_version:7.4.3`) but exposes a `garnet_version` field and does *not* implement every command.** Detect the real server early with `HELLO`, and don't trust the `redis_version` string alone — `redis_mode`/version can look like Redis while commands are missing.

Observed on Garnet 2.0.1 (adapt rather than fail):

- **`RANDOMKEY` is unavailable** (`ERR unknown command`) — so the default sampler doesn't work. Fall back to **`SCAN`** (cursored, `COUNT`) for sampling. For a small keyspace, a **full `SCAN` enumeration is exact, cheap, and better than sampling** — do that and report 100% coverage instead of extrapolating. On a **large** keyspace where `RANDOMKEY` is missing *and* full enumeration is impractical there is no cheap unbiased sampler: a partial `SCAN` returns keys in hash-bucket order, so stopping early **biases** the sample. Either run `SCAN` across the full cursor (capping the per-key *probing*, not the scan itself) and sample from what you collect, or accept a partial-`SCAN` sample and **state the bias** in the report.
- **`OBJECT ENCODING` (and `OBJECT HELP`) are unavailable** — you can't report internal encoding; omit that column and note it as unavailable.
- **`INFO keyspace` may be empty** even when `DBSIZE` > 0 — rely on `DBSIZE` for the key count.
- Still available and used as normal: `SCAN`, `TYPE`, `MEMORY USAGE`, `TTL`, the O(1) size commands (`HLEN`/`SCARD`/…), and `HGETALL`/`HRANDFIELD`/`LRANGE`/etc. for content.

The same "detect, then degrade gracefully and say which probes were unavailable" approach applies to any RESP server that under-implements the command set.

## Procedure

### 0. Scope & connect

`HELLO` (identify the real server — watch for `garnet_version` or other non-Redis markers; see *Server compatibility*), then `INFO server`, `INFO keyspace`, `INFO memory`, `INFO replication`, and `DBSIZE`. Note the server/RESP version, total key count, used memory, and whether a replica is available.

**Cluster:** if `INFO`/`CLUSTER INFO` shows cluster mode, `DBSIZE`/`RANDOMKEY`/`SCAN` are **per-node**. Either iterate each master node (sample and sum), or clearly state the summary is for the single node you connected to. Don't present one node's numbers as the whole cluster.

### 1. Choose the sample size

Default sample = **min(5% of `DBSIZE`, cap)**, where the cap defaults to **~2,000 keys**. Both the **fraction and the cap are user-overridable** — accept e.g. "sample 10%" or "up to 20000 keys" or "sample exactly 500". Report the actual sample size and what fraction of the DB it represents, since all extrapolations depend on it.

Draw keys with `resp-cli -r <N> randomkey`. `RANDOMKEY` samples **with replacement**, so de-duplicate the drawn keys (and note that a high duplicate rate implies a small or skewed keyspace).

### 2. Probe each sampled key

For each distinct sampled key collect:

- `TYPE key` — string / hash / list / set / zset / stream.
- `OBJECT ENCODING key` — internal representation (`int`, `embstr`, `raw`, `listpack`, `intset`, `hashtable`, `skiplist`, `quicklist`, `stream`, …); reveals small-vs-large internal layout.
- **Element size**, O(1) per type: `STRLEN` (string), `HLEN` (hash), `LLEN` (list), `SCARD` (set), `ZCARD` (zset), `XLEN` (stream).
- **Memory**: `MEMORY USAGE key SAMPLES 5` — true bytes incl. overhead (additive to the element counts; the two answer different questions). Fall back gracefully if the command is disabled.
- `TTL key` — to report the volatile vs. persistent split.

**Performance:** one `resp-cli` process per command is fine for small samples but slow for thousands. When the sample is large, build the probe commands and **pipeline them through `redis-cli`** (`printf '...\n' | redis-cli ...`), keeping `resp-cli` for the scoping/interactive steps. The sample cap is what keeps this bounded — honor it.

### 3. Tokenize key patterns

Split each key into segments on `:` (Redis convention) and `/`. Replace **variable** segments with a typed placeholder, then group keys by their tokenized pattern and count members.

Collapse a segment when it looks like an identifier. In rough priority (most likely first), but cast a **wider net than just these**:

- integers → `{int}`
- UUIDs / GUIDs → `{uuid}`
- hex strings (e.g. ≥8 hex chars) → `{hex}`
- ULIDs / base62 / base64-ish tokens, long random-looking strings → `{id}`
- epoch timestamps / date-like segments → `{ts}` / `{date}`
- **high-cardinality fallback**: a segment position that takes many distinct values across the sample (relative to its siblings) is almost certainly an id even if it doesn't match a shape above — collapse it to `{var}`.

Keep low-cardinality, stable segments literal (`users`, `orders`, `cache`, `session`). Report patterns with estimated full-DB counts (sample count ÷ sample fraction) and flag that they're estimates.

### 4. Classify content

**This step reads real values — only run it with user consent (see Hard safety rules). Without consent, skip it and deliver a structure-only summary.**

- **Strings**: use `STRLEN`, then `GETRANGE key 0 200` to peek (never a bare `GET` on a large/unknown value). Classify: integer counter (`^-?\d+$` and/or `OBJECT ENCODING` = `int`), JSON (`{`/`[` and parses), XML (`<…>`), base64/binary blob, or plain text. Note approximate value-size distribution.
- **Aggregates**: sample a few members rather than reading the whole structure. **Avoid `HGETALL`/`SMEMBERS`/`LRANGE key 0 -1` on unknown-size aggregates** — they're unbounded, so they're slow on large keys and pull far more data into context than needed. Size-gate first (`HLEN`/`SCARD`/`LLEN`), then use the random/ranged samplers: `HRANDFIELD key 5 WITHVALUES`, `SRANDMEMBER key 5`, `ZRANDMEMBER key 5 WITHSCORES`, `LRANGE key 0 4`, `XRANGE key - + COUNT 5` — and classify field names / element contents the same way.

### 5. Report

Produce a concise summary:

- **Connection & scope**: target (host/port/db, replica?), `DBSIZE`, used memory, RESP version, cluster note; sample size and fraction.
- **Key patterns**: table of tokenized patterns → estimated count, dominant type, dominant encoding, example raw key.
- **Where the data is**: top patterns/types **by count** and, separately, **by estimated total size** (per-key `MEMORY USAGE` × extrapolation). Call out the count-vs-size divergence.
- **Content**: per major pattern, what the values are (counter / JSON / XML / blob / text) with an example shape (redact obvious secrets/PII in examples).
- **Caveats**: sampling error, with-replacement duplicates, per-node vs. cluster-wide, any commands that were unavailable.

## Notes

- Everything here is an **estimate** from a sample — say so, and give the sample fraction alongside extrapolated numbers.
- `redis-cli --bigkeys` / `--memkeys` are complementary built-ins for the size question, but they don't do pattern tokenization or content classification — this skill's value is the patterns + content, so use the built-ins only as a cross-check.
- See `AGENTS.md` for the `resp-cli`-over-`redis-cli` preference and the broader project context.
