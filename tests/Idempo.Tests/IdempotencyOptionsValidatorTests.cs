namespace Idempo.Tests;

public class IdempotencyOptionsValidatorTests
{
    private readonly IdempotencyOptionsValidator _validator = new();

    [Fact]
    public void Default_options_are_valid()
    {
        var result = _validator.Validate(null, new IdempotencyOptions());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void CompletedTtl_shorter_than_PendingTimeout_fails()
    {
        var options = new IdempotencyOptions
        {
            PendingTimeout = TimeSpan.FromMinutes(5),
            CompletedTtl = TimeSpan.FromMinutes(1)
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("CompletedTtl"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_HeaderName_fails(string headerName)
    {
        var result = _validator.Validate(null, new IdempotencyOptions { HeaderName = headerName });
        Assert.True(result.Failed);
    }

    [Fact]
    public void Empty_Methods_fails()
    {
        var result = _validator.Validate(null, new IdempotencyOptions { Methods = new HashSet<string>() });
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_MaxKeyLength_fails(int maxKeyLength)
    {
        var result = _validator.Validate(null, new IdempotencyOptions { MaxKeyLength = maxKeyLength });
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_MaxRequestBodyBytes_fails(long maxBytes)
    {
        var result = _validator.Validate(null, new IdempotencyOptions { MaxRequestBodyBytes = maxBytes });
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_PendingTimeout_fails(int seconds)
    {
        var result = _validator.Validate(null, new IdempotencyOptions { PendingTimeout = TimeSpan.FromSeconds(seconds) });
        Assert.True(result.Failed);
    }
}
