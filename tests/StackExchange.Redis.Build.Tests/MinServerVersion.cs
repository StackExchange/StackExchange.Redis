using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Build.Tests;

/// <summary>
/// Version gating: an analyzer cannot see the server, so a project can declare its floor and get only the
/// suggestions it can act on.
/// </summary>
public class MinServerVersion : Verifier<TransactionAnalyzer>
{
    private const string CompareAndSet =
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.StringEqual(key, "old"))|};
                _ = tran.StringSetAsync(key, "new");
                await tran.ExecuteAsync();
            }
        }
        """;

    private const string ConditionalArgument =
        """
        using StackExchange.Redis;
        using System.Threading.Tasks;
        class C
        {
            public async Task M(IDatabase db, RedisKey key)
            {
                var tran = db.CreateTransaction();
                {|#0:tran.AddCondition(Condition.KeyNotExists(key))|};
                _ = tran.StringSetAsync(key, "value");
                await tran.ExecuteAsync();
            }
        }
        """;

    [Fact]
    // the default: nobody has said anything about servers, so show the suggestion. Silence by default would
    // hide the rule from exactly the people who have not thought about this yet
    public Task Unset_ShowsVersionGatedSuggestion() => VerifyAsync(
        CompareAndSet,
        Diagnostic("SER301").WithLocation(0));

    [Fact]
    public Task NewerThanRequired_ShowsSuggestion() => VerifyWithMinServerVersionAsync(
        CompareAndSet,
        "8.6",
        Diagnostic("SER301").WithLocation(0));

    [Fact]
    // exactly the required version counts as supported
    public Task ExactlyRequired_ShowsSuggestion() => VerifyWithMinServerVersionAsync(
        CompareAndSet,
        "8.4",
        Diagnostic("SER301").WithLocation(0));

    [Fact]
    // the point of the whole exercise: compare-and-set needs 8.4, so do not suggest it to someone on 7.4
    public Task OlderThanRequired_HidesSuggestion() => VerifyWithMinServerVersionAsync(
        CompareAndSet,
        "7.4");

    [Fact]
    // ... but the version-free family must survive the same setting, which is why they have separate IDs
    public Task OlderThanRequired_StillShowsVersionFreeSuggestion() => VerifyWithMinServerVersionAsync(
        ConditionalArgument,
        "2.8",
        Diagnostic("SER300").WithLocation(0));

    [Fact]
    // a major-only value is a reasonable thing to write
    public Task MajorOnly_IsUnderstood() => VerifyWithMinServerVersionAsync(
        CompareAndSet,
        "7");

    [Fact]
    // a patch component is accepted and ignored rather than rejected
    public Task PatchComponent_IsIgnored() => VerifyWithMinServerVersionAsync(
        CompareAndSet,
        "8.4.1",
        Diagnostic("SER301").WithLocation(0));

    [Fact]
    // an unreadable value falls back to showing everything: silently hiding suggestions over a typo would be
    // near-impossible to diagnose from the outside
    public Task Unparseable_ShowsEverything() => VerifyWithMinServerVersionAsync(
        CompareAndSet,
        "not-a-version",
        Diagnostic("SER301").WithLocation(0));

    [Fact]
    // the version reaches the message, so the reader knows what "newer" means without following the link
    public Task Message_NamesTheRequiredVersion() => VerifyAsync(
        CompareAndSet,
        Diagnostic("SER301").WithLocation(0).WithArguments(
            "Condition.StringEqual",
            "StringSetAsync",
            "StringSet[Async](key, value, ValueCondition.Equal(expected))",
            "8.4"));
}
