using FunQuery.Enums;

namespace FunQuery.AspNetCore.Tests;

/// <summary>
/// M5.2: one status mapping for every error code, and internal errors never leak details.
/// </summary>
public class QueryErrorStatusTests
{
    [Theory]
    [InlineData(QueryErrorCode.InputTooLong, 413)]
    [InlineData(QueryErrorCode.RequestBodyTooLarge, 413)]
    [InlineData(QueryErrorCode.UnsupportedMediaType, 415)]
    [InlineData(QueryErrorCode.ExecutionTimeout, 408)]
    [InlineData(QueryErrorCode.ResultTooLarge, 422)]
    [InlineData(QueryErrorCode.SourceTooLarge, 422)]
    [InlineData(QueryErrorCode.InternalError, 500)]
    [InlineData(QueryErrorCode.InvalidRequest, 400)]
    [InlineData(QueryErrorCode.UnknownSource, 400)]
    [InlineData(QueryErrorCode.InvalidParameter, 400)]
    [InlineData(QueryErrorCode.OrderRequired, 400)]
    [InlineData(QueryErrorCode.TypeMismatch, 400)]
    [InlineData(QueryErrorCode.TooManyTokens, 400)]
    [InlineData(QueryErrorCode.MaxDepthExceeded, 400)]
    [InlineData(QueryErrorCode.UnknownFunction, 400)]
    public void MapsEachCodeToItsHttpStatus(QueryErrorCode code, int expected)
    {
        Assert.Equal(expected, FunQueryStatusCodes.For(code));
    }

    [Fact]
    public void InputErrorKeepsTheCodeMessageAndPosition()
    {
        var error = new QueryException(
            QueryErrorCode.TypeMismatch,
            "Cannot compare string with int.",
            17,
            3);

        var (response, status) = QueryErrorResults.From(error);

        Assert.Equal(400, status);
        Assert.Equal("TYPE_MISMATCH", response.Code);
        Assert.Equal("Cannot compare string with int.", response.Message);
        Assert.Equal(17, response.Position);
        Assert.Equal(3, response.Length);
    }

    [Fact]
    public void InternalErrorHidesTheMessageAndPosition()
    {
        var error = new QueryException(
            QueryErrorCode.InternalError,
            "secret detail that must not reach the client",
            5,
            3);

        var (response, status) = QueryErrorResults.From(error);

        Assert.Equal(500, status);
        Assert.Equal("INTERNAL_ERROR", response.Code);
        Assert.Equal(QueryErrorResults.InternalMessage, response.Message);
        Assert.Null(response.Position);
        Assert.Null(response.Length);
    }

    [Fact]
    public void UnexpectedExceptionBecomesAGenericInternalError()
    {
        var (response, status) = QueryErrorResults.Unexpected();

        Assert.Equal(500, status);
        Assert.Equal("INTERNAL_ERROR", response.Code);
        Assert.Equal(QueryErrorResults.InternalMessage, response.Message);
        Assert.Null(response.Position);
        Assert.Null(response.Length);
    }

    [Fact]
    public void AnErrorWithoutAPositionOmitsIt()
    {
        var error = new QueryException(QueryErrorCode.InvalidRequest, "The 'q' parameter is required.");

        var (response, _) = QueryErrorResults.From(error);

        Assert.Null(response.Position);
        Assert.Null(response.Length);
    }
}
