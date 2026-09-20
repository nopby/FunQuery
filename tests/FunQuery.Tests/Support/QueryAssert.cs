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
        Assert.Equal(expected, exception.Code);
        return exception;
    }

    /// <summary>
    /// Like <see cref="Fails(QueryErrorCode, Action)"/>, and also checks where the error is reported:
    /// the start <paramref name="position"/> and the <paramref name="length"/> of the offending input.
    /// </summary>
    public static QueryException Fails(
        QueryErrorCode expected,
        int position,
        int length,
        Action action)
    {
        var exception = Fails(expected, action);
        Assert.Equal(position, exception.Position);
        Assert.Equal(length, exception.Length);
        return exception;
    }
}
