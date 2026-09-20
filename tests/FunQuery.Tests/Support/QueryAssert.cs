using System.Reflection;
using FunQuery.Enums;

namespace FunQuery.Tests.Support;

internal static class QueryAssert
{
    /// <summary>
    /// Asserts that <paramref name="action"/> throws a <see cref="QueryException"/> carrying
    /// <paramref name="expected"/>, and returns the exception for further assertions.
    /// </summary>
    public static QueryException Fails(QueryErrorCode expected, Action action)
    {
        var exception = Assert.ThrowsAny<QueryException>(action);
        Assert.Equal(expected, CodeOf(exception));
        return exception;
    }

    // The error code is located by type rather than by property name, so these tests do not
    // depend on how QueryException names it. If it is called 'Code', this can be simplified
    // to 'exception.Code'.
    private static QueryErrorCode CodeOf(QueryException exception)
    {
        var property = typeof(QueryException)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(p => p.PropertyType == typeof(QueryErrorCode));

        if (property is null)
        {
            throw new InvalidOperationException(
                "QueryException has no property of type QueryErrorCode.");
        }

        return (QueryErrorCode)property.GetValue(exception)!;
    }
}
