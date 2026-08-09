using Zara.Core.Results;

namespace Zara.Core.Tests.Results;

public class ResultTests
{
    [Fact]
    public void Success_HasValueAndNoError()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_HasErrorAndAccessingValueThrows()
    {
        var error = ZaraError.NotFound("missing");
        var result = Result<int>.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Success_AccessingErrorThrows()
    {
        var result = Result<int>.Success(1);

        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void ImplicitConversions_WrapValueAndError()
    {
        Result<int> fromValue = 7;
        Result<int> fromError = ZaraError.AccessDenied("nope");

        Assert.True(fromValue.IsSuccess);
        Assert.Equal(7, fromValue.Value);
        Assert.True(fromError.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, fromError.Error.Code);
    }

    [Fact]
    public void TryGetValue_ReturnsTrueAndValueOnSuccess()
    {
        var result = Result<string>.Success("hi");

        bool ok = result.TryGetValue(out var value);

        Assert.True(ok);
        Assert.Equal("hi", value);
    }

    [Fact]
    public void TryGetValue_ReturnsFalseAndDefaultOnFailure()
    {
        var result = Result<string>.Failure(ZaraError.NotFound("missing"));

        bool ok = result.TryGetValue(out var value);

        Assert.False(ok);
        Assert.Null(value);
    }

    [Fact]
    public void Match_InvokesTheCorrectBranch()
    {
        var success = Result<int>.Success(10);
        var failure = Result<int>.Failure(ZaraError.NotFound("x"));

        Assert.Equal("ok:10", success.Match(v => $"ok:{v}", e => $"err:{e.Code}"));
        Assert.Equal("err:NotFound", failure.Match(v => $"ok:{v}", e => $"err:{e.Code}"));
    }

    [Fact]
    public void Map_TransformsSuccessAndPassesFailureThrough()
    {
        var success = Result<int>.Success(3);
        var failure = Result<int>.Failure(ZaraError.NotFound("x"));

        Result<string> mappedSuccess = success.Map(v => $"n={v}");
        Result<string> mappedFailure = failure.Map(v => $"n={v}");

        Assert.True(mappedSuccess.IsSuccess);
        Assert.Equal("n=3", mappedSuccess.Value);
        Assert.True(mappedFailure.IsFailure);
        Assert.Equal(ErrorCode.NotFound, mappedFailure.Error.Code);
    }

    [Fact]
    public void NonGenericResult_SuccessAndFailure()
    {
        var success = Result.Success();
        var failure = Result.Failure(ZaraError.InvalidPath("bad"));

        Assert.True(success.IsSuccess);
        Assert.True(failure.IsFailure);
        Assert.Equal(ErrorCode.InvalidPath, failure.Error.Code);
    }

    [Fact]
    public void ZaraError_ToString_IncludesCodeAndMessage()
    {
        var error = new ZaraError(ErrorCode.Timeout, "took too long");

        Assert.Equal("Timeout: took too long", error.ToString());
    }
}
