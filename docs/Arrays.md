# Redis Arrays

Redis Arrays provide sparse arrays of arbitrary Redis values with unsigned array indexes and a notional write head. SE.Redis exposes the array API as experimental Redis 8.8 APIs; callers should expect details to change while the server feature is still in preview.

## Prerequisites

Arrays require Redis 8.8 or later. The APIs are marked with the `SER006` experimental warning.

## Basic Usage

Use `ArraySetAsync` and `ArrayGetAsync` to write and read individual cells:

```csharp
var db = conn.GetDatabase();
RedisKey key = "events";

bool inserted = await db.ArraySetAsync(key, 0, "created");
RedisValue value = await db.ArrayGetAsync(key, 0);
RedisValue missing = await db.ArrayGetAsync(key, 1);

Console.WriteLine(inserted); // True when the cell did not previously have a value
Console.WriteLine(value);    // created
Console.WriteLine(missing.IsNull); // True
```

Array indexes use `RedisArrayIndex`, with implicit conversions from `int`, `long`, and `ulong`. This allows normal small indexes to be used directly, while still allowing the full unsigned index range when needed.

```csharp
await db.ArraySetAsync(key, 42, "answer");
await db.ArraySetAsync(key, new RedisArrayIndex(10_000_000UL), "large index");
```

## Sparse Arrays

Arrays are sparse: unset cells do not have values. `ArrayLengthAsync` reports the notional length, which is the highest used index plus one. `ArrayCountAsync` reports only cells that currently have values.

```csharp
await db.KeyDeleteAsync(key);

await db.ArraySetAsync(key, 0, "a");
await db.ArraySetAsync(key, 10, "b");

RedisArrayIndex length = await db.ArrayLengthAsync(key); // 11
RedisArrayIndex count = await db.ArrayCountAsync(key);   // 2
```

## Setting Multiple Values

To write a contiguous range, pass the first index and the values:

```csharp
int inserted = await db.ArraySetAsync(key, 0, ["a", "b", "c"]);
```

To write multiple specific indexes, use `RedisArrayEntry` values:

```csharp
await db.ArraySetAsync(key,
[
    new RedisArrayEntry(0, "alpha"),
    new RedisArrayEntry(5, "bravo"),
    new RedisArrayEntry(100, "charlie"),
]);
```

The returned `int` is the number of cells that were newly filled.

## Reading Multiple Values

Read selected indexes with `ArrayGetAsync`:

```csharp
RedisValue[] values = await db.ArrayGetAsync(key, [0, 5, 6, 100]);
```

Read a range with `ArrayGetRangeAsync`. Ranges can be read forward or backward:

```csharp
RedisValue[] forward = await db.ArrayGetRangeAsync(key, 0, 5);
RedisValue[] reverse = await db.ArrayGetRangeAsync(key, 5, 0);
```

For sparse arrays, use `ArrayScanAsync` to return only populated cells in a range:

```csharp
RedisArrayEntry[] entries = await db.ArrayScanAsync(key, 0, 100, limit: 50);

foreach (var entry in entries)
{
    Console.WriteLine($"{entry.Index}: {entry.Value}");
}
```

## Deleting Values

Delete a single cell with `ArrayDeleteAsync`:

```csharp
bool removed = await db.ArrayDeleteAsync(key, 5);
```

Delete multiple specific cells by index:

```csharp
int removedCount = await db.ArrayDeleteAsync(key, [0, 5, 100]);
```

Delete one or more ranges:

```csharp
await db.ArrayDeleteRangeAsync(key, 10, 20);

await db.ArrayDeleteRangeAsync(key,
[
    new RedisArrayRange(100, 199),
    new RedisArrayRange(500, 599),
]);
```

## Searching

Use `ArrayGrepRequest` with `ArrayGrepAsync` to search values. When `Start` or `End` is not specified, the server's open-ended lower or upper bound is used.

```csharp
var request = new ArrayGrepRequest
{
    Limit = 10,
};
request.AddPredicate(ArrayGrepRequest.Predicate.Match("error"));

RedisArrayEntry[] matches = await db.ArrayGrepAsync(key, request);

foreach (var match in matches)
{
    Console.WriteLine(match.Index);
}
```

Set `IncludeValues` to return values along with the matching indexes:

```csharp
var request = new ArrayGrepRequest
{
    IncludeValues = true,
};
request.AddPredicate(ArrayGrepRequest.Predicate.Regex("^ERR[0-9]+"));

RedisArrayEntry[] matches = await db.ArrayGrepAsync(key, request);

foreach (var match in matches)
{
    Console.WriteLine($"{match.Index}: {match.Value}");
}
```

Multiple predicates can be combined. By default, predicates are combined as `OR`; set `IsIntersection` to combine them as `AND`.

```csharp
var request = new ArrayGrepRequest
{
    IsIntersection = true,
};
request.AddPredicate(ArrayGrepRequest.Predicate.Match("redis"));
request.AddPredicate(ArrayGrepRequest.Predicate.Glob("*array*"));

RedisArrayEntry[] matches = await db.ArrayGrepAsync(key, request);
```

## Write Head

Arrays have a write head used by insert operations. `ArrayInsertAsync` writes at the current write head and advances it.

```csharp
RedisArrayIndex first = await db.ArrayInsertAsync(key, "first");
RedisArrayIndex second = await db.ArrayInsertAsync(key, "second");

RedisArrayIndex? next = await db.ArrayNextAsync(key);
```

Move the write head with `ArraySeekAsync`:

```csharp
bool moved = await db.ArraySeekAsync(key, 1_000);
RedisArrayIndex written = await db.ArrayInsertAsync(key, "later");
```

`ArrayLastItemsAsync` reads recent values from the array tail:

```csharp
RedisValue[] last = await db.ArrayLastItemsAsync(key, count: 10);
RedisValue[] lastReversed = await db.ArrayLastItemsAsync(key, count: 10, reverse: true);
```

## Ring Buffers

Use `ArrayRingAsync` to keep at most a fixed number of cells and wrap writes around that capacity:

```csharp
for (int i = 0; i < 10; i++)
{
    await db.ArrayRingAsync(key, maxLength: 5, value: i);
}

RedisArrayIndex count = await db.ArrayCountAsync(key); // 5
```

## Operations and Info

Use `ArrayOperationAsync` for simple server-side operations over a range:

```csharp
RedisValue sum = await db.ArrayOperationAsync(key, 0, 10, ArrayOperation.Sum);
RedisValue used = await db.ArrayOperationAsync(key, 0, 10, ArrayOperation.Used);
RedisValue matches = await db.ArrayOperationAsync(key, 0, 10, ArrayOperation.Match, "error");
```

Use `ArrayInfoAsync` for metadata:

```csharp
ArrayInfo info = await db.ArrayInfoAsync(key);

Console.WriteLine($"Count: {info.Count}");
Console.WriteLine($"Length: {info.Length}");
Console.WriteLine($"Next insert index: {info.NextInsertIndex}");
```
