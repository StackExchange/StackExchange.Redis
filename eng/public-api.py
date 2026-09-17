#!/usr/bin/env python3
"""Sort the PublicApiAnalyzers tracking files, and optionally promote unshipped API to shipped.

Two jobs, deliberately separate:

*Sorting* (always). The analyzer does not care about order and its code fix appends, so these files
accrete in whatever order entries were added, and drift out of any order they are put into. Nothing
keeps them sorted between runs - this is what does, run at a moment somebody is already editing them.

*Promotion* (``--promote`` only). Moves every entry from ``PublicAPI.Unshipped.txt`` into its sibling
``PublicAPI.Shipped.txt``. "Shipped" means released and frozen, so doing this mid-cycle would freeze
API that has not shipped and cannot then be changed without it looking like a break. It is therefore
opt in, and belongs to the release: ship the package, then promote.

    eng/public-api.py                # sort in place, promote nothing
    eng/public-api.py --promote      # the release step: fold unshipped into shipped, and sort
    eng/public-api.py --check        # CI: write nothing, exit 1 if anything is unsorted
"""

import argparse
import os
import sys

# Modifiers the analyzer writes ahead of a signature. Stripped for sorting so a type's surface stays in
# one block: sorting the raw line groups by modifier instead, splitting every type across an
# abstract/override/static/virtual block and a bare one.
MODIFIERS = ("abstract ", "override ", "static ", "virtual ", "readonly ", "const ", "sealed ")


def sort_key(line):
    s = line
    # an experimental annotation - [SER009]Foo.Bar - sorts with its symbol, not as punctuation
    if s.startswith("["):
        close = s.find("]")
        if close > 0:
            s = s[close + 1:]
    changed = True
    while changed:
        changed = False
        for m in MODIFIERS:
            if s.startswith(m):
                s = s[len(m):]
                changed = True
    # tie-break on the whole line so the order is total and stable
    return (s, line)


class ApiFile:
    def __init__(self, path):
        self.path = path
        raw = open(path, "rb").read()
        self.bom = raw.startswith(b"\xef\xbb\xbf")
        text = raw.decode("utf-8-sig")
        self.newline = "\r\n" if "\r\n" in text else "\n"
        lines = text.replace("\r\n", "\n").split("\n")
        # header directives (#nullable enable) stay at the top; blank lines are not content, and go
        self.header = [l for l in lines if l.startswith("#")]
        self.entries = [l for l in lines if l and not l.startswith("#")]

    def render(self, entries):
        text = self.newline.join(self.header + entries) + self.newline
        return (b"\xef\xbb\xbf" if self.bom else b"") + text.encode("utf-8")

    def write(self, entries):
        open(self.path, "wb").write(self.render(entries))


def find_shipped(root):
    out = []
    for dirpath, dirnames, filenames in os.walk(os.path.join(root, "src")):
        dirnames[:] = [d for d in dirnames if d not in ("obj", "bin")]
        if "PublicAPI.Shipped.txt" in filenames:
            out.append(os.path.join(dirpath, "PublicAPI.Shipped.txt"))
    return sorted(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--promote", action="store_true",
                    help="move unshipped entries into shipped; do this only just after a release")
    ap.add_argument("--check", action="store_true",
                    help="write nothing; exit 1 if any file would change")
    args = ap.parse_args()

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    shipped_paths = find_shipped(root)
    if not shipped_paths:
        print(f"no PublicAPI.Shipped.txt found under {root}/src", file=sys.stderr)
        return 2

    changed, problems = [], []

    for shipped_path in shipped_paths:
        shipped = ApiFile(shipped_path)
        unshipped_path = os.path.join(os.path.dirname(shipped_path), "PublicAPI.Unshipped.txt")
        unshipped = ApiFile(unshipped_path) if os.path.exists(unshipped_path) else None

        rel = os.path.relpath(shipped_path, root)
        entries = list(shipped.entries)
        promoted = []

        if args.promote and unshipped and unshipped.entries:
            promoted = list(unshipped.entries)
            # an entry in both files is not something to silently dedupe: the analyzer rejects
            # duplicates (RS0025), and it means one of the two was hand-edited
            clash = [e for e in promoted if e in set(entries)]
            if clash:
                problems.append(f"{rel}: {len(clash)} entr(y/ies) already shipped, e.g. {clash[0]}")
                continue
            entries += promoted

        new_shipped = sorted(entries, key=sort_key)
        if shipped.render(new_shipped) != shipped.render(shipped.entries) or promoted:
            note = f" (+{len(promoted)} promoted)" if promoted else ""
            changed.append(rel + note)
            if not args.check:
                shipped.write(new_shipped)

        if unshipped:
            rel_un = os.path.relpath(unshipped_path, root)
            # promoted entries leave; whatever remains is sorted too, so that appending to it from two
            # branches collides less often
            new_unshipped = [] if promoted else sorted(unshipped.entries, key=sort_key)
            if unshipped.render(new_unshipped) != unshipped.render(unshipped.entries):
                if not promoted:
                    changed.append(rel_un)
                if not args.check:
                    unshipped.write(new_unshipped)

    for p in problems:
        print(f"ERROR: {p}", file=sys.stderr)
    if problems:
        return 2

    if not changed:
        print("public API files are sorted; nothing to do.")
        return 0

    for c in changed:
        print(("would change: " if args.check else "updated: ") + c)

    if args.check:
        print("run 'eng/public-api.py' to fix.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
