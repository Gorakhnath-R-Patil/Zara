using Zara.Search.Query;

namespace Zara.Search.Tests.Query;

public class SelectivityEstimatorTests
{
    private readonly SelectivityEstimator _sut = new();

    [Fact]
    public void Estimate_EmptyQuery_IsFullyUnselective()
    {
        Assert.Equal(1.0, _sut.Estimate(new StructuredQuery()));
    }

    [Fact]
    public void Estimate_NameTerm_IsMoreSelectiveThanEmptyQuery()
    {
        var withName = new StructuredQuery { NameTerms = ["resume"] };

        Assert.True(_sut.Estimate(withName) < _sut.Estimate(new StructuredQuery()));
    }

    [Fact]
    public void Estimate_MorePredicates_IsMoreSelective()
    {
        var one = new StructuredQuery { Extensions = ["pdf"] };
        var two = new StructuredQuery { Extensions = ["pdf"], Size = new SizeRange(1000, null) };

        Assert.True(_sut.Estimate(two) < _sut.Estimate(one));
    }

    [Fact]
    public void Estimate_AlwaysStaysWithinZeroToOne()
    {
        var everything = new StructuredQuery
        {
            NameTerms = ["a"],
            Extensions = ["pdf"],
            TypeClasses = ["image"],
            Size = new SizeRange(1, 2),
            Modified = new DateRange(DateTimeOffset.UtcNow, null),
            Created = new DateRange(DateTimeOffset.UtcNow, null),
            Accessed = new DateRange(DateTimeOffset.UtcNow, null),
            Attributes = ["hidden"],
            Depth = 1,
        };

        double result = _sut.Estimate(everything);

        Assert.InRange(result, 0.0, 1.0);
    }
}
