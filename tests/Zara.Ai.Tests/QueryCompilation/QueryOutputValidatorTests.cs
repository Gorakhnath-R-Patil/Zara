using Zara.Ai.QueryCompilation;

namespace Zara.Ai.Tests.QueryCompilation;

public class QueryOutputValidatorTests
{
    private static LlmQueryOutput Valid() => new() { Intent = "find_files", Confidence = 0.9 };

    [Fact]
    public void Validate_MinimalValidOutput_Passes()
    {
        var result = QueryOutputValidator.Validate(Valid());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("delete_everything")]
    [InlineData("Find_Files ")]
    public void Validate_UnknownOrMissingIntent_Fails(string? intent)
    {
        var output = Valid();
        output.Intent = intent;

        var result = QueryOutputValidator.Validate(output);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("image")]
    [InlineData("pdf")]
    public void Validate_KnownFileType_Passes(string fileType)
    {
        var output = Valid();
        output.FileTypes = [fileType];

        Assert.True(QueryOutputValidator.Validate(output).IsValid);
    }

    [Fact]
    public void Validate_UnknownFileType_Fails()
    {
        var output = Valid();
        output.FileTypes = ["executable"];

        Assert.False(QueryOutputValidator.Validate(output).IsValid);
    }

    [Fact]
    public void Validate_UnknownPathScope_Fails()
    {
        var output = Valid();
        // A path the model invented rather than one of the enumerated
        // locations — exactly what §14.3 says must never happen, and what
        // this layer catches if the schema constraint were ever bypassed.
        output.PathScope = [@"C:\Users\bob\SecretStuff"];

        Assert.False(QueryOutputValidator.Validate(output).IsValid);
    }

    [Fact]
    public void Validate_UnknownSort_Fails()
    {
        var output = Valid();
        output.Sort = "alphabetical";

        Assert.False(QueryOutputValidator.Validate(output).IsValid);
    }

    [Fact]
    public void Validate_NullSort_Passes()
    {
        var output = Valid();
        output.Sort = null;

        Assert.True(QueryOutputValidator.Validate(output).IsValid);
    }

    [Fact]
    public void Validate_NegativeMinBytes_Fails()
    {
        var output = Valid();
        output.MinBytes = -1;

        Assert.False(QueryOutputValidator.Validate(output).IsValid);
    }

    [Fact]
    public void Validate_MinBytesGreaterThanMaxBytes_Fails()
    {
        var output = Valid();
        output.MinBytes = 1000;
        output.MaxBytes = 100;

        Assert.False(QueryOutputValidator.Validate(output).IsValid);
    }

    [Fact]
    public void Validate_MinBytesEqualToMaxBytes_Passes()
    {
        var output = Valid();
        output.MinBytes = 100;
        output.MaxBytes = 100;

        Assert.True(QueryOutputValidator.Validate(output).IsValid);
    }

    [Fact]
    public void Validate_InvalidDateString_Fails()
    {
        var output = Valid();
        output.ModifiedAfter = "not a date";

        Assert.False(QueryOutputValidator.Validate(output).IsValid);
    }

    [Fact]
    public void Validate_ModifiedAfterLaterThanModifiedBefore_Fails()
    {
        var output = Valid();
        output.ModifiedAfter = "2024-06-01";
        output.ModifiedBefore = "2024-01-01";

        Assert.False(QueryOutputValidator.Validate(output).IsValid);
    }

    [Fact]
    public void Validate_ValidDateRange_Passes()
    {
        var output = Valid();
        output.ModifiedAfter = "2024-01-01";
        output.ModifiedBefore = "2024-06-01";

        Assert.True(QueryOutputValidator.Validate(output).IsValid);
    }

    [Fact]
    public void Validate_FutureModifiedDate_Fails()
    {
        var output = Valid();
        output.ModifiedAfter = DateTimeOffset.UtcNow.AddYears(1).ToString("yyyy-MM-dd");

        Assert.False(QueryOutputValidator.Validate(output).IsValid);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Validate_ConfidenceOutsideZeroToOne_Fails(double confidence)
    {
        var output = Valid();
        output.Confidence = confidence;

        Assert.False(QueryOutputValidator.Validate(output).IsValid);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(0.5)]
    public void Validate_ConfidenceAtOrWithinBounds_Passes(double confidence)
    {
        var output = Valid();
        output.Confidence = confidence;

        Assert.True(QueryOutputValidator.Validate(output).IsValid);
    }

    // ── Clamp ────────────────────────────────────────────────────────────────

    [Fact]
    public void Clamp_LimitAboveMax_IsClampedTo500()
    {
        var output = Valid();
        output.Limit = 10_000;

        QueryOutputValidator.Clamp(output);

        Assert.Equal(500, output.Limit);
    }

    [Fact]
    public void Clamp_LimitBelowOne_IsClampedToOne()
    {
        var output = Valid();
        output.Limit = 0;

        QueryOutputValidator.Clamp(output);

        Assert.Equal(1, output.Limit);
    }

    [Fact]
    public void Clamp_NullLimit_DefaultsTo200()
    {
        var output = Valid();
        output.Limit = null;

        QueryOutputValidator.Clamp(output);

        Assert.Equal(200, output.Limit);
    }

    [Fact]
    public void Clamp_TooManyKeywords_IsTruncatedToTwelve()
    {
        var output = Valid();
        output.Keywords = Enumerable.Range(0, 20).Select(i => $"kw{i}").ToList();

        QueryOutputValidator.Clamp(output);

        Assert.Equal(12, output.Keywords!.Count);
    }

    [Fact]
    public void Clamp_OverlongSemanticQuery_IsTruncatedTo300Chars()
    {
        var output = Valid();
        output.SemanticQuery = new string('x', 500);

        QueryOutputValidator.Clamp(output);

        Assert.Equal(300, output.SemanticQuery!.Length);
    }

    [Fact]
    public void Clamp_ValidValues_AreLeftUnchanged()
    {
        var output = Valid();
        output.Limit = 50;
        output.Keywords = ["resume", "japan"];

        QueryOutputValidator.Clamp(output);

        Assert.Equal(50, output.Limit);
        Assert.Equal(["resume", "japan"], output.Keywords);
    }
}
