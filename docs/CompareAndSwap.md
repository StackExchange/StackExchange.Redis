# Compare-And-Swap / Compare-And-Delete (CAS/CAD)

Redis 8.4 introduces atomic Compare-And-Swap (CAS) and Compare-And-Delete (CAD) operations, allowing you to conditionally modify
or delete values based on their current state. SE.Redis exposes these features through the `ValueCondition` abstraction.

## Prerequisites

- Redis 8.4.0 or later

## Overview

Traditional Redis operations like `SET NX` (set if not exists) and `SET XX` (set if exists) only check for key existence.
CAS/CAD operations go further by allowing you to verify the **actual value** before making changes, enabling true atomic
compare-and-swap semantics, without requiring Lua scripts or complex `MULTI`/`WATCH`/`EXEC` usage.

The `ValueCondition` struct supports several condition types:

- **Existence checks**: `Always`, `Exists`, `NotExists` (equivalent to the traditional `When` enum)
- **Value equality**: `Equal(value)`, `NotEqual(value)` - compare the full value (uses `IFEQ`/`IFNE`)
- **Digest equality**: `DigestEqual(value)`, `DigestNotEqual(value)` - compare XXH3 64-bit hash (uses `IFDEQ`/`IFDNE`)

## Basic Value Equality Checks

Use value equality when you need to verify the exact current value before updating or deleting:

```csharp
var db = connection.GetDatabase();
var key = "user:session:12345";

// Set a value only if it currently equals a specific value
var currentToken = "old-token-abc";
var newToken = "new-token-xyz";

var wasSet = await db.StringSetAsync(
    key,
    newToken,
    when: ValueCondition.Equal(currentToken)
);

if (wasSet)
{
    Console.WriteLine("Token successfully rotated");
}
else
{
    Console.WriteLine("Token mismatch - someone else updated it");
}
```

### Conditional Delete

Delete a key only if it contains a specific value:

```csharp
var lockToken = "my-unique-lock-token";

// Only delete if the lock still has our token
var wasDeleted = await db.StringDeleteAsync(
    "resource:lock",
    when: ValueCondition.Equal(lockToken)
);

if (wasDeleted)
{
    Console.WriteLine("Lock released successfully");
}
else
{
    Console.WriteLine("Lock was already released or taken by someone else");
}
```

(see also the [Lock Operations section](#lock-operations) below)

## Digest-Based Checks

For large values, comparing the full value can be inefficient. Digest-based checks use XXH3 64-bit hashing to compare values efficiently:

```csharp
var key = "document:content";
var largeDocument = GetLargeDocumentBytes(); // e.g., 10MB

// Calculate digest locally
var expectedDigest = ValueCondition.CalculateDigest(largeDocument);

// Update only if the document hasn't changed
var newDocument = GetUpdatedDocumentBytes();
var wasSet = await db.StringSetAsync(
    key,
    newDocument,
    when: expectedDigest
);
```

### Retrieving Server-Side Digests

You can retrieve the digest of a value stored in Redis without fetching the entire value:

```csharp
// Get the digest of the current value
var digest = await db.StringDigestAsync(key);

if (digest.HasValue)
{
    Console.WriteLine($"Current digest: {digest.Value}");

    // Later, use this digest for conditional operations
    var wasDeleted = await db.StringDeleteAsync(key, when: digest.Value);
}
else
{
    Console.WriteLine("Key does not exist");
}
```

## Negating Conditions

Use the `!` operator to negate any condition:

```csharp
var expectedValue = "old-value";

// Set only if the value is NOT equal to expectedValue
var wasSet = await db.StringSetAsync(
    key,
    "new-value",
    when: !ValueCondition.Equal(expectedValue)
);

// Equivalent to:
var wasSet2 = await db.StringSetAsync(
    key,
    "new-value",
    when: ValueCondition.NotEqual(expectedValue)
);
```

## Converting Between Value and Digest Conditions

Convert a value condition to a digest condition for efficiency:

```csharp
var valueCondition = ValueCondition.Equal("some-value");

// Convert to digest-based check
var digestCondition = valueCondition.AsDigest();

// Now uses IFDEQ instead of IFEQ
var wasSet = await db.StringSetAsync(key, "new-value", when: digestCondition);
```

## Parsing Digests

If you receive a XXH3 digest as a hex string (e.g., from external systems), you can parse it:

```csharp
// Parse from hex string
var digestCondition = ValueCondition.ParseDigest("e34615aade2e6333");

// Use in conditional operations
var wasSet = await db.StringSetAsync(key, newValue, when: digestCondition);
```

## Lock Operations

StackExchange.Redis automatically uses CAS/CAD for lock operations when Redis 8.4+ is available, providing better performance and atomicity:

```csharp
var lockKey = "resource:lock";
var lockToken = Guid.NewGuid().ToString();
var lockExpiry = TimeSpan.FromSeconds(30);

// Take a lock (uses NX internally)
if (await db.LockTakeAsync(lockKey, lockToken, lockExpiry))
{
    try
    {
        // Do work while holding the lock

        // Extend the lock (uses CAS internally on Redis 8.4+)
        if (!(await db.LockExtendAsync(lockKey, lockToken, lockExpiry))
        {
            // Failed to extend the lock - it expired, or was forcibly taken against our will
            throw new InvalidOperationException("Lock extension failed - check expiry duration is appropriate.");
        }

        // Do more work...
    }
    finally
    {
        // Release the lock (uses CAD internally on Redis 8.4+)
        await db.LockReleaseAsync(lockKey, lockToken);
    }
}
```

On Redis 8.4+, `LockExtend` uses `SET` with `IFEQ` and `LockRelease` uses `DELEX` with `IFEQ`, eliminating
the need for transactions.

## Common Patterns

### Optimistic Locking

Implement optimistic concurrency control for updating data:

```csharp
async Task<bool> UpdateUserProfileAsync(string userId, Func<UserProfile, UserProfile> updateFunc)
{
    var key = $"user:profile:{userId}";

    // Read current value
    var currentJson = await db.StringGetAsync(key);
    if (currentJson.IsNull)
    {
        return false; // User doesn't exist
    }

    var currentProfile = JsonSerializer.Deserialize<UserProfile>(currentJson!);
    var updatedProfile = updateFunc(currentProfile);
    var updatedJson = JsonSerializer.Serialize(updatedProfile);

    // Attempt to update only if value hasn't changed
    var wasSet = await db.StringSetAsync(
        key,
        updatedJson,
        when: ValueCondition.Equal(currentJson)
    );

    return wasSet; // Returns false if someone else modified it
}

// Usage with retry logic
int maxRetries = 10;
for (int i = 0; i < maxRetries; i++)
{
    if (await UpdateUserProfileAsync(userId, profile =>
    {
        profile.LastLogin = DateTime.UtcNow;
        return profile;
    }))
    {
        break; // Success
    }

    // Retry with exponential backoff
    await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, i) * 10));
}
```

### Session Token Rotation

Safely rotate session tokens with atomic verification:

```csharp
async Task<bool> RotateSessionTokenAsync(string sessionId, string expectedToken)
{
    var key = $"session:{sessionId}";
    var newToken = GenerateSecureToken();

    // Only rotate if the current token matches
    var wasRotated = await db.StringSetAsync(
        key,
        newToken,
        expiry: TimeSpan.FromHours(24),
        when: ValueCondition.Equal(expectedToken)
    );

    return wasRotated;
}
```

### Large Document Updates with Digest

For large documents, use digests to avoid transferring the full value:

```csharp
async Task<bool> UpdateLargeDocumentAsync(string docId, byte[] newContent)
{
    var key = $"document:{docId}";

    // Get just the digest, not the full document
    var currentDigest = await db.StringDigestAsync(key);

    if (!currentDigest.HasValue)
    {
        return false; // Document doesn't exist
    }

    // Update only if digest matches (document unchanged)
    var wasSet = await db.StringSetAsync(
        key,
        newContent,
        when: currentDigest.Value
    );

    return wasSet;
}
```

## Performance Considerations

### Value vs. Digest Checks

- **Value equality** (`IFEQ`/`IFNE`): Best for small values (< 1KB). Sends the full value to Redis for comparison.
- **Digest equality** (`IFDEQ`/`IFDNE`): Best for large values. Only sends a 16-character hex digest (8 bytes).

```csharp
// For small values (session tokens, IDs, etc.)
var condition = ValueCondition.Equal(smallValue);

// For large values (documents, images, etc.)
var condition = ValueCondition.DigestEqual(largeValue);
// or
var condition = ValueCondition.CalculateDigest(largeValueBytes);
```

## See Also

- [Transactions](Transactions.md) - For multi-key atomic operations
- [Keys and Values](KeysValues.md) - Understanding Redis data types
- [Redis CAS/CAD Documentation](https://redis.io/docs/latest/commands/set/) - Redis 8.4 SET command with IFEQ/IFNE/IFDEQ/IFDNE modifiers
