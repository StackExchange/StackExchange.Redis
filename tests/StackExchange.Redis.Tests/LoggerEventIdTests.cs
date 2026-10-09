using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Guards the source-generated <see cref="LoggerMessageAttribute"/> events in <c>LoggerExtensions</c>:
/// every event must carry an explicit <c>EventId</c>, and no two events may share one. Event ids are
/// what consumers filter and alert on, so a clash silently merges unrelated events downstream; this
/// has happened when parallel branches both took "the next free number".
/// </summary>
public class LoggerEventIdTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public void EventIdsAreExplicitAndUnique()
    {
        var events = (
            from method in typeof(LoggerExtensions).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            let attrib = method.GetCustomAttribute<LoggerMessageAttribute>()
            where attrib is not null
            orderby attrib.EventId, method.Name
            select (Method: method, Attrib: attrib)).ToList();

        Assert.NotEmpty(events); // if reflection stops finding them, the test is vacuous

        var problems = new List<string>();

        // LoggerMessageAttribute.EventId defaults to -1 when not specified; the generator then emits 0,
        // which is indistinguishable from "unknown" to a consumer
        foreach (var (method, attrib) in events.Where(e => e.Attrib.EventId < 0))
        {
            problems.Add($"{Describe(method)} has no explicit EventId");
        }

        foreach (var group in events.Where(e => e.Attrib.EventId >= 0).GroupBy(e => e.Attrib.EventId).Where(g => g.Count() > 1))
        {
            problems.Add($"EventId {group.Key} is used {group.Count()} times: {string.Join(", ", group.Select(e => Describe(e.Method)))}");
        }

        int max = events.Max(e => e.Attrib.EventId);
        Log($"{events.Count} events; highest EventId {max}; next free EventId {max + 1}");
        foreach (var problem in problems)
        {
            Log(problem);
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static string Describe(MethodInfo method)
    {
        // skip the "this ILogger" parameter; show the rest so overloads are distinguishable
        var args = method.GetParameters().Skip(1).Select(p => p.ParameterType.Name);
        return $"{method.Name}({string.Join(", ", args)})";
    }
}
