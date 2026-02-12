# Redis Vector Sets

Redis Vector Sets provide efficient storage and similarity search for vector data. SE.Redis provides a strongly-typed API for working with vector sets.

## Prerequisites

### Redis Version

Vector Sets require Redis 8.0 or later.

## Quick Start

Note that the vectors used in these examples are small for illustrative purposes. In practice, you would commonly use much
larger vectors. The API is designed to efficiently handle large vectors - in particular, the use of `ReadOnlyMemory<T>`
rather than arrays allows you to work with vectors in "pooled" memory buffers (such as `ArrayPool<T>`), which can be more
efficient than creating arrays - or even working with raw memory for example memory-mapped-files.

### Adding Vectors

Add vectors to a vector set using `VectorSetAddAsync`:

```csharp
var db = conn.GetDatabase();
var key = "product-embeddings";

// Create a vector (e.g., from an ML model)
var vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f };

// Add a member with its vector
var request = VectorSetAddRequest.Member("product-123", vector.AsMemory());
bool added = await db.VectorSetAddAsync(key, request);
```

### Adding Vectors with Attributes

You can attach JSON metadata to vectors for filtering:

```csharp
var vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
var request = VectorSetAddRequest.Member(
    "product-123",
    vector.AsMemory(),
    attributesJson: """{"category":"electronics","price":299.99}"""
);
await db.VectorSetAddAsync(key, request);
```

### Similarity Search

Find similar vectors using `VectorSetSimilaritySearchAsync`:

```csharp
// Search by an existing member
var query = VectorSetSimilaritySearchRequest.ByMember("product-123");
query.Count = 10;
query.WithScores = true;

using var results = await db.VectorSetSimilaritySearchAsync(key, query);
if (results is not null)
{
    foreach (var result in results.Value.Results)
    {
        Console.WriteLine($"Member: {result.Member}, Score: {result.Score}");
    }
}
```

Or search by a vector directly:

```csharp
var queryVector = new[] { 0.15f, 0.25f, 0.35f, 0.45f };
var query = VectorSetSimilaritySearchRequest.ByVector(queryVector.AsMemory());
query.Count = 10;
query.WithScores = true;

using var results = await db.VectorSetSimilaritySearchAsync(key, query);
```

### Filtered Search

Use JSON path expressions to filter results:

```csharp
var query = VectorSetSimilaritySearchRequest.ByVector(queryVector.AsMemory());
query.Count = 10;
query.FilterExpression = "$.category == 'electronics' && $.price < 500";
query.WithAttributes = true; // Include attributes in results

using var results = await db.VectorSetSimilaritySearchAsync(key, query);
```

See [Redis filtered search documentation](https://redis.io/docs/latest/develop/data-types/vector-sets/filtered-search/) for filter syntax.

## Vector Set Operations

### Getting Vector Set Information

```csharp
var info = await db.VectorSetInfoAsync(key);
if (info != null)
{
    Console.WriteLine($"Dimension: {info.Value.Dimension}");
    Console.WriteLine($"Length: {info.Value.Length}");
    Console.WriteLine($"Quantization: {info.Value.Quantization}");
}
```

### Checking Membership

```csharp
bool exists = await db.VectorSetContainsAsync(key, "product-123");
```

### Removing Members

```csharp
bool removed = await db.VectorSetRemoveAsync(key, "product-123");
```

### Getting Random Members

```csharp
// Get a single random member
var member = await db.VectorSetRandomMemberAsync(key);

// Get multiple random members
var members = await db.VectorSetRandomMembersAsync(key, count: 5);
```

## Range Queries

### Getting Members by Lexicographical Range

Retrieve members in lexicographical order:

```csharp
// Get all members
using var allMembers = await db.VectorSetRangeAsync(key);
// ... access allMembers.Span, etc

// Get members in a specific range
using var rangeMembers = await db.VectorSetRangeAsync(
    key,
    start: "product-100",
    end: "product-200",
    count: 50
);
// ... access rangeMembers.Span, etc

// Exclude boundaries
using var members = await db.VectorSetRangeAsync(
    key,
    start: "product-100",
    end: "product-200",
    exclude: Exclude.Both
);
// ... access members.Span, etc
```

### Enumerating Large Result Sets

For large vector sets, use enumeration to process results in batches:

```csharp
await foreach (var member in db.VectorSetRangeEnumerateAsync(key, count: 100))
{
    Console.WriteLine($"Processing: {member}");
}
```

The enumeration of results is done in batches, so that the client does not need to buffer the entire result set in memory;
if you exit the loop early, the client and server will stop processing and sending results. This also supports async cancellation:

```csharp
using var cts = new CancellationTokenSource(); // cancellation not shown

await foreach (var member in db.VectorSetRangeEnumerateAsync(key, count: 100)
    .WithCancellation(cts.Token))
{
    // ...
}
```

## Advanced Configuration

### Quantization

Control vector compression:

```csharp
var request = VectorSetAddRequest.Member("product-123", vector.AsMemory());
request.Quantization = VectorSetQuantization.Int8;  // Default
// or VectorSetQuantization.None
// or VectorSetQuantization.Binary
await db.VectorSetAddAsync(key, request);
```

### Dimension Reduction

Use projection to reduce vector dimensions:

```csharp
var request = VectorSetAddRequest.Member("product-123", vector.AsMemory());
request.ReducedDimensions = 128; // Reduce from original dimension
await db.VectorSetAddAsync(key, request);
```

### HNSW Parameters

Fine-tune the HNSW index:

```csharp
var request = VectorSetAddRequest.Member("product-123", vector.AsMemory());
request.MaxConnections = 32;           // M parameter (default: 16)
request.BuildExplorationFactor = 400;  // EF parameter (default: 200)
await db.VectorSetAddAsync(key, request);
```

### Search Parameters

Control search behavior:

```csharp
var query = VectorSetSimilaritySearchRequest.ByVector(queryVector.AsMemory());
query.SearchExplorationFactor = 500;  // Higher = more accurate, slower
query.Epsilon = 0.1;                  // Only return similarity >= 0.9
query.UseExactSearch = true;          // Use linear scan instead of HNSW
await db.VectorSetSimilaritySearchAsync(key, query);
```

## Working with Vector Data

### Retrieving Vectors

Get the approximate vector for a member:

```csharp
using var vectorLease = await db.VectorSetGetApproximateVectorAsync(key, "product-123");
if (vectorLease != null)
{
    ReadOnlySpan<float> vector = vectorLease.Value.Span;
    // Use the vector data
}
```

### Managing Attributes

Get and set JSON attributes:

```csharp
// Get attributes
var json = await db.VectorSetGetAttributesJsonAsync(key, "product-123");

// Set attributes
await db.VectorSetSetAttributesJsonAsync(
    key,
    "product-123",
    """{"category":"electronics","updated":"2024-01-15"}"""
);
```

### Graph Links

Inspect HNSW graph connections:

```csharp
// Get linked members
using var links = await db.VectorSetGetLinksAsync(key, "product-123");
if (links != null)
{
    foreach (var link in links.Value.Span)
    {
        Console.WriteLine($"Linked to: {link}");
    }
}

// Get links with similarity scores
using var linksWithScores = await db.VectorSetGetLinksWithScoresAsync(key, "product-123");
if (linksWithScores != null)
{
    foreach (var link in linksWithScores.Value.Span)
    {
        Console.WriteLine($"Linked to: {link.Member}, Score: {link.Score}");
    }
}
```

## Memory Management

Vector operations return `Lease<T>` for efficient memory pooling. Always dispose leases:

```csharp
// Using statement (recommended)
using var results = await db.VectorSetSimilaritySearchAsync(key, query);

// Or explicit disposal
var results = await db.VectorSetSimilaritySearchAsync(key, query);
try
{
    // Use results
}
finally
{
    results?.Dispose();
}
```

## Performance Considerations

### Batch Operations

For bulk inserts, consider using pipelining:

```csharp
var batch = db.CreateBatch();
var tasks = new List<Task<bool>>();

foreach (var (member, vector) in vectorData)
{
    var request = VectorSetAddRequest.Member(member, vector.AsMemory());
    tasks.Add(batch.VectorSetAddAsync(key, request));
}

batch.Execute();
await Task.WhenAll(tasks);
```

### Search Optimization

- Use **quantization** to reduce memory usage and improve search speed
- Tune **SearchExplorationFactor** based on accuracy vs. speed requirements
- Use **filters** to reduce the search space
- Consider **dimension reduction** for very high-dimensional vectors

### Range Query Pagination

Prefer enumeration for large result sets to avoid loading everything into memory:

```csharp
// Good: loads results in batches, processes items individually
await foreach (var member in db.VectorSetRangeEnumerateAsync(key))
{
    await ProcessMemberAsync(member);
}

// Avoid: loads all results at once
using var allMembers1 = await db.VectorSetRangeAsync(key);

// Avoid: loads resultsin batches, but still loads everything into memory at once
var allMembers2 = await VectorSetRangeEnumerateAsync(key).ToArrayAsync();
```

## Common Patterns

### Semantic Search

```csharp
// 1. Store document embeddings
var embedding = await GetEmbeddingFromMLModel(document);
var request = VectorSetAddRequest.Member(
    documentId,
    embedding.AsMemory(),
    attributesJson: $$"""{"title":"{{document.Title}}","date":"{{document.Date}}"}"""
);
await db.VectorSetAddAsync("documents", request);

// 2. Search for similar documents
var queryEmbedding = await GetEmbeddingFromMLModel(searchQuery);
var query = VectorSetSimilaritySearchRequest.ByVector(queryEmbedding.AsMemory());
query.Count = 10;
query.WithScores = true;
query.WithAttributes = true;

using var results = await db.VectorSetSimilaritySearchAsync("documents", query);
```

### Recommendation System

```csharp
// Find similar items based on an item the user liked
var query = VectorSetSimilaritySearchRequest.ByMember(userLikedItemId);
query.Count = 20;
query.FilterExpression = "$.inStock == true && $.price < 100";
query.WithScores = true;

using var recommendations = await db.VectorSetSimilaritySearchAsync("products", query);
```

## See Also

- [Redis Vector Sets Documentation](https://redis.io/docs/latest/develop/data-types/vector-sets/)
- [HNSW Algorithm](https://arxiv.org/abs/1603.09320)
- [Filtered Search Syntax](https://redis.io/docs/latest/develop/data-types/vector-sets/filtered-search/)

