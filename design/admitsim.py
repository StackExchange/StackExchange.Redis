"""Single-threaded model of the v4 client cache under RespFest's Zipf stream: no Redis, no .NET, no concurrency.

Written to ask whether default ~= OnFirstMiss at 64 callers came from the access stream or from parallelism: the
stream (it reproduces 72.5% either way, with zero refusals), and through it a doorkeeper that aged only by first
sightings. `count_all` is the fix: every miss ages the filter.

Model (mirrors RespClientCache):
  * doorkeeper: 2^20-bit Bloom filter, two probes from a 32-bit hash, cleared after 131,072 first sightings - or,
    with count_all, 131,072 misses (sized from MaxBytes / EntryOverheadBytes = 32 MiB / 256)
  * storage: slabs in fill order; the oldest slab is evicted whole when over budget; hits touch nothing
  * budget: 32 MiB counting whole 1 MiB slabs plus 256 bytes per live entry
"""
import random
import sys

sys.path.insert(0, "/home/marc/code/RespFest")
from harness.keyseq import zipf  # noqa: E402

import struct  # noqa: E402

KEYSPACE = 200_000
SLAB = 1 << 20
ENTRY = 1024 + 30  # reply + key, both stored in the slab
OVERHEAD = 256
BUDGET = 32 << 20
BITS = 1 << 20
CLEAR_AFTER = BITS // 8

rng = random.Random(1)
HASH = [rng.getrandbits(32) for _ in range(KEYSPACE)]  # a well-mixed hash per request


def run(stream, n_ops, measure_from, admission, clear_after=CLEAR_AFTER, count_all=False):
    words = bytearray(BITS // 8)
    recorded = 0
    live = {}  # key -> slab id
    slabs = {}  # slab id -> list of keys stored there
    oldest = 0
    current = 0
    used = 0  # bytes used in the current slab
    slabs[0] = []
    live_count = 0
    hits = misses = stores = refused = 0

    def seen_before(h):
        nonlocal recorded, words
        a = h & (BITS - 1)
        b = ((h >> 16) | (h << 16)) & 0xFFFFFFFF & (BITS - 1)
        if words[a >> 3] >> (a & 7) & 1 and words[b >> 3] >> (b & 7) & 1:
            if count_all:  # age by every query, not only by first sightings
                recorded += 1
                if recorded >= clear_after:
                    recorded = 0
                    words = bytearray(BITS // 8)
            return True
        words[a >> 3] |= 1 << (a & 7)
        words[b >> 3] |= 1 << (b & 7)
        recorded += 1
        if recorded >= clear_after:
            recorded = 0
            words = bytearray(BITS // 8)
        return False

    for t in range(n_ops):
        k = stream(t)
        measuring = t >= measure_from
        if k in live:
            if measuring:
                hits += 1
            continue
        if measuring:
            misses += 1
        if admission != "first" and not seen_before(HASH[k]):
            if measuring:
                refused += 1
            continue
        # store into the current slab
        if used + ENTRY > SLAB:
            current += 1
            slabs[current] = []
            used = 0
        slabs[current].append(k)
        used += ENTRY
        live[k] = current
        live_count += 1
        if measuring:
            stores += 1
        # evict oldest sealed slabs while over budget
        while (current - oldest + 1) * SLAB + live_count * OVERHEAD > BUDGET and oldest < current:
            for victim in slabs.pop(oldest):
                if live.get(victim) == oldest:
                    del live[victim]
                    live_count -= 1
            oldest += 1
    total = hits + misses
    return hits / total, stores, refused, len(live)


def true_zipf(count, seed):
    """Independent Zipf(0.99) draws over the keyspace: no table, so nothing replays."""
    import bisect
    cumulative, total = [], 0.0
    for rank in range(1, KEYSPACE + 1):
        total += rank ** -0.99
        cumulative.append(total)
    r = random.Random(seed)
    return [min(bisect.bisect_left(cumulative, r.random() * total), KEYSPACE - 1) for _ in range(count)]


def main():
    seq = struct.unpack(f"<{1 << 20}I", zipf(KEYSPACE, 0.99, length=1 << 20, seed=20261010))  # the league's 1M table
    fresh = true_zipf(13_000_000, 11)
    k = 13_000_000
    cases = [
        ("true zipf, 13M", lambda t: fresh[t]),
        ("1M table x64 callers", lambda t: seq[((t % 64) * 16384 + t // 64) % len(seq)]),
    ]
    policies = [
        ("first", {}),
        ("repeat: clear @131k new (today)", {}),
        ("repeat: clear @131k misses", {"count_all": True}),
        ("repeat: clear @64k misses", {"count_all": True, "clear_after": 65536}),
        ("repeat: clear @256k misses", {"count_all": True, "clear_after": 262144}),
    ]
    for name, stream in cases:
        for policy, kw in policies:
            hit, stores, refused, size = run(stream, k, k // 3, "first" if policy == "first" else "repeat", **kw)
            print(f"{name:22} {policy:34} hit {hit:6.1%}  stores {stores:>9,}  refused {refused:>9,}", flush=True)


main()
