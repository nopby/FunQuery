using FunQuery.Enums;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>
/// Hostile input must end in a controlled <see cref="QueryException"/>, never in a crash.
/// A stack overflow cannot be caught in .NET, so these tests are the safety net for the depth guards.
/// </summary>
public class AdversarialInputTests
{
    // Length and token limits are relaxed so that the depth guards, not the size limits,
    // are what stops the input.
    private static readonly QueryLimits Relaxed = new()
    {
        MaxInputLength = 5_000_000,
        MaxTokens = 5_000_000,
    };

    private static void AssertDepthExceeded(string input) =>
        QueryAssert.Fails(
            QueryErrorCode.MaxDepthExceeded,
            () => QueryPipeline.Analyze(input, limits: Relaxed));

    [Fact]
    public void DeeplyNestedParentheses_AreRejected()
    {
        AssertDepthExceeded("$source(" + new string('(', 50_000) + "1" + new string(')', 50_000) + ")");
    }

    [Fact]
    public void DeeplyNestedArrays_AreRejected()
    {
        AssertDepthExceeded("$source(" + new string('[', 50_000) + new string(']', 50_000) + ")");
    }

    [Fact]
    public void DeeplyNestedObjects_AreRejected()
    {
        var open = string.Concat(Enumerable.Repeat("{a:", 50_000));
        var close = new string('}', 50_000);

        AssertDepthExceeded("$source([" + open + "1" + close + "])");
    }

    [Fact]
    public void DeeplyNestedFunctionCalls_AreRejected()
    {
        var open = string.Concat(Enumerable.Repeat("$source(", 50_000));
        var close = new string(')', 50_000);

        AssertDepthExceeded(open + "[1]" + close);
    }

    [Fact]
    public void VeryLongChainOfCalls_IsRejectedByTheAnalyzer()
    {
        // The parser builds a chain iteratively; the analyzer walks it recursively.
        AssertDepthExceeded("$source([{id:1}])" + string.Concat(Enumerable.Repeat(".$filter(id eq 1)", 20_000)));
    }

    [Fact]
    public void VeryLongChainOfAnd_IsRejectedByTheAnalyzer()
    {
        var conditions = string.Join(" and ", Enumerable.Repeat("id eq 1", 50_000));

        AssertDepthExceeded("$source([{id:1}]).$filter(" + conditions + ")");
    }

    [Fact]
    public void MegabyteSizedInput_IsRejectedByDefaultLimitsBeforeLexing()
    {
        var input = new string('(', 1_000_000);

        QueryAssert.Fails(QueryErrorCode.InputTooLong, () => QueryPipeline.Analyze(input));
    }

    [Fact]
    public void ManyTokensWithinTheLengthLimit_AreRejected()
    {
        var input = "$source([" + string.Concat(Enumerable.Repeat("1,", 1_000)) + "1])";

        QueryAssert.Fails(
            QueryErrorCode.TooManyTokens,
            () => QueryPipeline.Analyze(input, limits: new QueryLimits { MaxInputLength = 10_000 }));
    }

    // ------------------------------------------------------------------
    // Fuzz-lite: seeded and deterministic, so a failure can always be reproduced.
    // Any exception other than a user-facing QueryException is a bug in FunQuery.
    // ------------------------------------------------------------------

    private static readonly string[] Fragments =
    [
        "$source", "$filter", "$x", "(", ")", "[", "]", "{", "}", ",", ":", ".", " ",
        "id", "name", "eq", "neq", "gt", "and", "or", "1", "2.5", "007", "'x'", "'", "#", "\"", "true", "false", "null",
    ];

    private static readonly string[] ValidQueries =
    [
        "$source([{id: 1, name: 'hello'}, {id: 2, name: 'world'}]).$filter(id eq 2)",
        "$source([{id: 1}]).$filter(id gte 1 and id lt 5 or id neq 3)",
        "($source([{a: 1.5, b: 'x'}])).$filter(a gt 1)",
        "$source([{id: 1, name: null}, {id: 2, name: 'x'}]).$filter(name neq null and id lte 2)",
    ];

    private static readonly QueryEngine Engine = new();

    private static string? Run(string input)
    {
        try
        {
            Engine.Execute(input).ToJson();
            return null;
        }
        catch (QueryException error) when (!error.IsInternal)
        {
            return null;
        }
        catch (Exception error)
        {
            return $"{error.GetType().Name}: {error.Message}   input: {input}";
        }
    }

    [Fact]
    public void RandomTokenSoup_NeverEscapesAsAnUnexpectedException()
    {
        var random = new Random(20260921);
        var problems = new List<string>();

        for (int i = 0; i < 4_000; i++)
        {
            var count = random.Next(1, 25);
            var input = string.Concat(Enumerable.Range(0, count)
                .Select(_ => Fragments[random.Next(Fragments.Length)]));

            var problem = Run(input);
            if (problem is not null)
                problems.Add(problem);
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void MutatedValidQueries_NeverEscapeAsAnUnexpectedException()
    {
        var random = new Random(20260922);
        var problems = new List<string>();

        for (int i = 0; i < 4_000; i++)
        {
            var text = ValidQueries[random.Next(ValidQueries.Length)];

            for (int edits = random.Next(1, 4); edits > 0 && text.Length > 0; edits--)
            {
                var at = random.Next(text.Length);

                text = random.Next(3) switch
                {
                    0 => text.Remove(at, 1),
                    1 => text.Insert(at, Fragments[random.Next(Fragments.Length)]),
                    _ => text.Remove(at, 1).Insert(at, Fragments[random.Next(Fragments.Length)]),
                };
            }

            var problem = Run(text);
            if (problem is not null)
                problems.Add(problem);
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void EveryPrefixOfAValidQuery_EndsInAControlledResult()
    {
        // Truncated input is the classic source of off-by-one crashes in hand-written parsers.
        foreach (var query in ValidQueries)
        {
            for (int length = 0; length <= query.Length; length++)
            {
                var problem = Run(query[..length]);

                Assert.Null(problem);
            }
        }
    }
}
