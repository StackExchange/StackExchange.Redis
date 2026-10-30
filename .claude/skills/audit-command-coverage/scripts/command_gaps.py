#!/usr/bin/env python3
"""Diff the upstream Redis command specs against StackExchange.Redis and the coverage ledger.

Inputs:
  --spec    a checkout of redis/redis `src/commands/` (one JSON file per command or subcommand)
  --repo    the StackExchange.Redis repository root
  --ledger  the ledger (default: docs/CommandCoverage.md under --repo)

A command counts as *covered* when its wire token is a `RedisCommand` enum member (or an
`[AsciiHash("...")]` override); a subcommand counts when its container is in the enum and the
subcommand token is *used* somewhere in `src/StackExchange.Redis`: either via its `RedisLiterals`
member (`RedisLiterals.GETKEYS`) or as a string literal. Definitions in RedisLiterals.cs alone
do not count.

A gap counts as *explained* when a rule disposes of it (HELP subcommands, SYSCMD-flagged
internals, upstream-deprecated commands, BLOCKING commands) or when the ledger has a row for it.

Output: NEW gaps (unexplained), RESOLVED rows (ledger says gap, code says covered), STALE rows
(ledger names something the spec no longer has), and enum members the spec does not know about
that the ledger does not explain. Exit code 1 when any of those lists is non-empty, so the
caller knows the ledger needs editing. `--all` dumps every gap with its disposition.
"""
import argparse
import json
import os
import re
import sys

RULES = (
    # (predicate, status, rule name)
    (lambda r: r["container"] and r["name"] == "HELP", "not-applicable", "help"),
    (lambda r: "SYSCMD" in r["doc_flags"], "not-applicable", "syscmd"),
    (lambda r: bool(r["deprecated_since"]), "deprecated", "deprecated"),
    (lambda r: "BLOCKING" in r["command_flags"], "policy", "blocking"),
)

ENUM_MEMBER = re.compile(r'(?:\[AsciiHash\("([^"]*)"\)\]\s*)?^\s*([A-Z_][A-Z0-9_]*)\s*,', re.M)
LEDGER_ROW = re.compile(r"^\|\s*`([A-Z][A-Z0-9 _-]*?)`\s*\|\s*([^|]*?)\s*\|\s*`?([a-z-]+)`?\s*\|", re.M)
LEDGER_SHA = re.compile(r"redis/redis@([0-9a-f]{7,40})")
SENTINEL_MEMBERS = {"", "NONE", "UNKNOWN"}


def load_spec(spec_dir):
    rows = []
    for file in sorted(os.listdir(spec_dir)):
        if not file.endswith(".json"):
            continue
        with open(os.path.join(spec_dir, file), encoding="utf-8") as f:
            doc = json.load(f)
        for name, spec in doc.items():
            container = spec.get("container")
            rows.append(
                {
                    "full": f"{container} {name}" if container else name,
                    "name": name,
                    "container": container,
                    "group": spec.get("group", "?"),
                    "since": spec.get("since", "?"),
                    "deprecated_since": spec.get("deprecated_since"),
                    "replaced_by": spec.get("replaced_by"),
                    "command_flags": spec.get("command_flags") or [],
                    "doc_flags": spec.get("doc_flags") or [],
                    "summary": spec.get("summary", ""),
                }
            )
    return rows


def load_enum_tokens(repo):
    path = os.path.join(repo, "src", "StackExchange.Redis", "Enums", "RedisCommand.cs")
    with open(path, encoding="utf-8-sig") as f:
        src = f.read()
    tokens = set()
    for m in ENUM_MEMBER.finditer(src):
        tokens.add(m.group(1) if m.group(1) is not None else m.group(2))
    return tokens - SENTINEL_MEMBERS


LITERAL_DECL = re.compile(r'(\w+)\s*=\s*RedisValue\.FromRaw\("([^"]*)"u8\)')


def load_source_blob(repo):
    """The library source minus RedisLiterals.cs, plus a map of wire token -> RedisLiterals identifier.

    RedisLiterals.cs *defines* tokens; only a use site (`RedisLiterals.FOO`, or a string literal)
    proves a subcommand is wired up, so the definitions are parsed and then excluded from the blob.
    """
    parts, literals = [], {}
    for root, _, files in os.walk(os.path.join(repo, "src", "StackExchange.Redis")):
        for file in files:
            if not file.endswith(".cs"):
                continue
            with open(os.path.join(root, file), encoding="utf-8-sig", errors="replace") as f:
                text = f.read()
            if file == "RedisLiterals.cs":
                for m in LITERAL_DECL.finditer(text):
                    literals.setdefault(m.group(2).upper(), set()).add(m.group(1))
            else:
                parts.append(text)
    return "\n".join(parts), literals


def load_ledger(path):
    with open(path, encoding="utf-8") as f:
        text = f.read()
    sha = LEDGER_SHA.search(text)
    rows = {m.group(1): m.group(3) for m in LEDGER_ROW.finditer(text)}
    return (sha.group(1) if sha else None), rows


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--spec", required=True, help="path to redis/redis src/commands")
    ap.add_argument("--repo", default=".", help="StackExchange.Redis repository root")
    ap.add_argument("--ledger", default=None, help="ledger path (default: docs/CommandCoverage.md)")
    ap.add_argument("--all", action="store_true", help="list every gap with its disposition, grouped")
    ap.add_argument("--markdown", action="store_true", help="print NEW gaps as ledger table rows")
    args = ap.parse_args()

    repo = os.path.abspath(args.repo)
    ledger_path = args.ledger or os.path.join(repo, "docs", "CommandCoverage.md")

    spec = load_spec(args.spec)
    tokens = load_enum_tokens(repo)
    blob, literals = load_source_blob(repo)
    ledger_sha, ledger = load_ledger(ledger_path)

    def has_literal(tok):
        # a use of the RedisLiterals member for this token, or the token as an inline UTF-8 literal
        # ("TOKEN"u8, which is how this codebase spells raw wire tokens); plain "TOKEN" strings are
        # too often log text, attribute arguments or unrelated enums to count
        for ident in literals.get(tok.upper(), ()):
            if re.search(r"\bRedisLiterals\." + re.escape(ident) + r"\b", blob):
                return True
        return re.search(r'"' + re.escape(tok) + r'"u8', blob) is not None

    def covered(r):
        if r["container"]:
            return r["container"] in tokens and has_literal(r["name"])
        return r["name"] in tokens

    def rule_for(r):
        for pred, status, rule in RULES:
            if pred(r):
                return status, rule
        return None, None

    spec_names = {r["full"] for r in spec}
    gaps, new, resolved = [], [], []
    for r in spec:
        is_covered = covered(r)
        in_ledger = r["full"] in ledger
        status, rule = rule_for(r)
        if in_ledger and ledger[r["full"]] == "implemented":
            # hand-written message the heuristic cannot see; the ledger vouches for it
            continue
        if is_covered:
            if in_ledger and ledger[r["full"]] != "enum-only":
                resolved.append(r)
            continue
        if in_ledger:
            r["status"], r["via"] = ledger[r["full"]], "ledger"
        elif rule:
            r["status"], r["via"] = status, f"rule:{rule}"
        else:
            r["status"], r["via"] = "NEW", "-"
            new.append(r)
        gaps.append(r)

    stale = sorted(name for name in ledger if name not in spec_names and name not in tokens)
    top_level = {r["name"] for r in spec if not r["container"]}
    enum_only = sorted(t for t in tokens if t not in top_level and t not in ledger)

    print(f"upstream spec: {len(spec)} commands; enum tokens: {len(tokens)}; gaps: {len(gaps)} "
          f"({len(new)} new, {sum(1 for g in gaps if g['via'] == 'ledger')} ledger, {sum(1 for g in gaps if g['via'].startswith('rule'))} rule)")
    print(f"ledger upstream: redis/redis@{ledger_sha or '?'}")

    def flags(r):
        out = []
        if "BLOCKING" in r["command_flags"]:
            out.append("blocking")
        if "ADMIN" in r["command_flags"]:
            out.append("admin")
        if "SYSCMD" in r["doc_flags"]:
            out.append("syscmd")
        if r["deprecated_since"]:
            out.append(f"deprecated {r['deprecated_since']}")
        return ", ".join(out)

    def section(title, rows):
        print(f"\n## {title} ({len(rows)})")
        for r in rows:
            print(f"  {r['full']:<32} since {str(r['since']):<8} {flags(r):<22} {r['summary'][:70]}")

    if args.all:
        by_group = {}
        for r in gaps:
            by_group.setdefault(r["group"], []).append(r)
        for g in sorted(by_group):
            print(f"\n## {g}")
            for r in by_group[g]:
                print(f"  {r['full']:<32} since {str(r['since']):<8} {r['status']:<15} {r['via']:<16} {flags(r)}")

    section("NEW: in the spec, not in the code, not explained", new)
    if args.markdown and new:
        print()
        for r in new:
            print(f"| `{r['full']}` | {r['since']} | `new` | {r['summary']} |")
    section("RESOLVED: ledger row, but the code now covers it (delete the row)", resolved)
    print(f"\n## STALE: ledger row, but the spec no longer has it ({len(stale)})")
    for name in stale:
        print(f"  {name}")
    print(f"\n## ENUM-ONLY: in the enum, not in the spec, not explained ({len(enum_only)})")
    for name in enum_only:
        print(f"  {name}")

    return 1 if (new or resolved or stale or enum_only) else 0


if __name__ == "__main__":
    sys.exit(main())
