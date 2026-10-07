#!/usr/bin/env python3
"""Regenerate the old-IDatabase -> grouped-API table in docs/LegacyApi.md.

The source of truth is the v4 IDatabase implementation itself (src/StackExchange.Redis/Database/
RedisDatabase*.cs), which forwards every member to a group method: so the table is read from what the
code does, not maintained by hand. Members that forward to a helper are followed through it; members that
are not commands, or that compose several commands, are listed in OVERRIDES with what to use instead.

    python3 eng/docs/api-map.py          # rewrite the table between the markers
    python3 eng/docs/api-map.py --check  # exit 1 if the table is stale (for CI, if wanted)
"""
import collections, glob, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DOC = os.path.join(ROOT, "docs", "LegacyApi.md")
BEGIN, END = "<!-- api-map:begin -->", "<!-- api-map:end -->"
GROUPS = ("Strings Hashes Lists Sets SortedSets Keys Streams Scripts Geospatial HyperLogLog Bitmaps "
          "Arrays VectorSets PubSub").split()

# old member -> what to write instead, where the forwarding cannot say it (not a command, a composite,
# or a member whose group spelling changed shape rather than name)
OVERRIDES = {
    "ListRightPopLeftPush": "`db.Lists.MoveAsync(source, destination, ListSide.Right, ListSide.Left)`",
    "GeoRadius": "`db.Geospatial.SearchAsync` (`GEORADIUS` is deprecated by the server in favour of `GEOSEARCH`)",
    "Execute": "`db.Context.SendAsync<T>($\"...\")` - see [Extending the client](Extending)",
    "ExecuteResp": "`db.Context.SendAsync<T>($\"...\")` - see [Extending the client](Extending)",
    "Ping": "unchanged; or `db.PingMeasureAsync()`, which takes a `CancellationToken`",
    "CreateBatch": "unchanged; or `db.BeginBatch()` ([SER014](exp/SER014), experimental)",
    "CreateTransaction": "unchanged; or `db.BeginTransaction()` ([SER014](exp/SER014), experimental)",
    "KeyDelete": "`db.Keys.UnlinkAsync` to keep its behaviour (`KeyDelete` sends `UNLINK` where the server has it); "
                 "`db.Keys.DeleteAsync` is a plain, blocking `DEL`",
    "ScriptEvaluate": "`db.Scripts.EvaluateAsync`, returning a `RespResult` rather than a `RedisResult`",
    "ScriptEvaluateResp": "`db.Scripts.EvaluateAsync`",
    "ScriptEvaluateReadOnly": "`db.Scripts.EvaluateReadOnlyAsync`, returning a `RespResult` rather than a `RedisResult`",
    "ScriptEvaluateReadOnlyResp": "`db.Scripts.EvaluateReadOnlyAsync`",
    "KeyExpire": "`db.Keys.ExpireAsync(key, Expiration)`; a null expiry is `db.Keys.PersistAsync`",
    "StringGetSet": "`db.Strings.SetAndGetAsync` (`GETSET` is deprecated by the server; `SET ... GET` needs 6.2)",
    "StringDecrement": "`db.Strings.IncrementAsync`, with the value negated",
    "HashDecrement": "`db.Hashes.IncrementAsync`, with the value negated",
    "SortedSetDecrement": "`db.SortedSets.IncrementAsync`, with the value negated",
    "SortedSetUpdate": "`db.SortedSets.AddAsync(..., when, change: true)` (`ZADD ... CH`)",
    "StringGetWithExpiry": "none: two commands; `db.Strings.GetAsync` and `db.Keys.TimeToLiveAsync` in a batch",
}
for name in ("LockTake", "LockRelease", "LockExtend", "LockQuery"):
    OVERRIDES[name] = "none: a composite helper, stays on `IDatabase`"
for name in ("IsConnected", "IdentifyEndpoint", "Wait", "WaitAll", "TryWait"):
    OVERRIDES[name] = None  # not commands: omitted from the table


def public_group_methods():
    text = "".join(open(p, encoding="utf-8").read() for p in glob.glob(
        os.path.join(ROOT, "src", "StackExchange.Redis", "PublicAPI", "**", "PublicAPI.*.txt"), recursive=True))
    found = collections.defaultdict(set)
    for group, method in re.findall(
            r"static StackExchange\.Redis\.(\w+)\.(\w+Async)\(this (?:in )?StackExchange\.Redis\.Resp\w+", text):
        found[group].add(method)
    return found


def forwarding():
    src = "".join(open(p, encoding="utf-8-sig").read() for p in sorted(glob.glob(
        os.path.join(ROOT, "src", "StackExchange.Redis", "Database", "RedisDatabase*.cs"))))
    direct, calls = collections.defaultdict(set), collections.defaultdict(set)
    for chunk in re.split(r"\n(?=\s{4,8}(?:public|private|internal|protected)\s)", src):
        head = re.match(r"\s*(public|private|internal|protected)\s+(?:static\s+|async\s+|override\s+|virtual\s+)*"
                        r"[\w<>\[\]?,. ()]*?\b(\w+)\s*(?:<[^>]*>)?\s*\(", chunk)
        if not head:
            continue
        visibility, name = head.groups()
        base = name[:-5] if name.endswith("Async") else name
        key = (visibility, base)
        direct[key] |= {f"{g}.{m}" for g, m in re.findall(r"\b_inner\.(\w+)\.(\w+)\(", chunk) if g in GROUPS}
        calls[key] |= set(re.findall(r"\b([A-Z]\w+)\(", chunk))
    known = {b for _, b in direct}

    def resolve(base, seen=()):
        out = set()
        for (visibility, b), groups in direct.items():
            if b != base:
                continue
            out |= groups
            if not groups:  # following helpers beside a direct call drags in fallbacks; see OVERRIDES
                for callee in calls[(visibility, b)]:
                    cb = callee[:-5] if callee.endswith("Async") else callee
                    if cb != base and cb not in seen and cb in known:
                        out |= resolve(cb, seen + (base,))
        return out

    return {b: resolve(b) for (v, b) in direct if v == "public"}


def to_public(target, public):
    group, method = target.split(".")
    if method in public[group]:
        return method
    # internal helpers are named after their public twin: ReadArray -> ReadAsync, GetWritableLease -> GetLeaseAsync
    stem = re.sub(r"(Array|Core|Result|DirectResult|Info)$", "", method).replace("Writable", "")
    for attempt in (stem, stem.removesuffix("Lease")):  # GetApproximateVectorWritableLease -> GetApproximateVectorAsync
        if attempt + "Async" in public[group]:
            return attempt + "Async"
        candidates = sorted((m for m in public[group] if m.startswith(attempt) and m.endswith("Async")), key=len)
        if candidates:
            return candidates[0]
    return None


def build():
    public, rows = public_group_methods(), collections.defaultdict(list)
    for old, targets in sorted(forwarding().items()):
        if old in OVERRIDES:
            if OVERRIDES[old] is not None:
                rows["Not a one-to-one rename"].append((old, OVERRIDES[old]))
            continue
        news = sorted({f"db.{t.split('.')[0]}.{m}" for t in targets if (m := to_public(t, public))})
        if not news:
            raise SystemExit(f"no public group method found for IDatabase.{old}; add it to OVERRIDES")
        group = news[0].split(".")[1]
        rows[group].append((old, ", ".join(f"`{n}`" for n in news)))
    lines = [BEGIN, "", "Generated from the v4 `IDatabase` implementation by `eng/docs/api-map.py`; "
             "each old name covers its `Async` twin and every overload.", ""]
    for group in [g for g in GROUPS if g in rows] + ["Not a one-to-one rename"]:
        lines += [f"**{group}**", "", "| `IDatabase` | grouped |", "|---|---|"]
        lines += [f"| `{old}` | {new} |" for old, new in rows[group]]
        lines.append("")
    lines.append(END)
    return "\n".join(lines)


if __name__ == "__main__":
    doc = open(DOC, encoding="utf-8").read()
    table = build()
    if BEGIN in doc:
        updated = doc[:doc.index(BEGIN)] + table + doc[doc.index(END) + len(END):]
    else:
        raise SystemExit(f"{DOC} has no {BEGIN} marker")
    if "--check" in sys.argv:
        sys.exit(0 if updated == doc else 1)
    open(DOC, "w", encoding="utf-8", newline="\n").write(updated)
