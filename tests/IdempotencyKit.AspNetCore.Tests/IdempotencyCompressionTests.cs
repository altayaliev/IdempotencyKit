using System.IO.Compression;
using System.Net;

namespace IdempotencyKit.AspNetCore.Tests;

/// <summary>
/// IdempotencyKit buffers the response into a <see cref="MemoryStream"/> by temporarily replacing
/// <c>context.Response.Body</c> — exactly the same trick ASP.NET Core's own response-compression
/// middleware uses. Nothing in the unit/contract test suite exercises both together, so this
/// verifies empirically what actually happens rather than assuming it's fine.
/// </summary>
public class IdempotencyCompressionTests
{
    [Fact]
    public async Task Replayed_response_is_correctly_compressed_and_decompresses_to_the_original_body()
    {
        using var host = new TestHost { EnableResponseCompression = true };
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");

        var first = new StringContent("{\"sku\":\"compressed-item\"}");
        first.Headers.Add("Idempotency-Key", "compressed-order");
        var firstResponse = await client.PostAsync("/orders", first);
        var firstDecoded = await DecodeAsync(firstResponse);

        var second = new StringContent("{\"sku\":\"compressed-item\"}");
        second.Headers.Add("Idempotency-Key", "compressed-order");
        var secondResponse = await client.PostAsync("/orders", second);
        var secondDecoded = await DecodeAsync(secondResponse);

        Assert.Equal(1, host.HandlerExecutionCount);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Contains("orderId", firstDecoded);
        Assert.Equal(firstDecoded, secondDecoded);

        // Prove compression actually engaged on both requests — otherwise the assertions above
        // would trivially pass even if compression silently never kicked in.
        Assert.Contains("gzip", firstResponse.Content.Headers.ContentEncoding);
        Assert.Contains("gzip", secondResponse.Content.Headers.ContentEncoding);
    }

    private static async Task<string> DecodeAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var contentEncoding = response.Content.Headers.ContentEncoding;

        if (contentEncoding.Contains("gzip"))
        {
            using var raw = new MemoryStream(bytes);
            using var gzip = new GZipStream(raw, CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            await gzip.CopyToAsync(decoded);
            return System.Text.Encoding.UTF8.GetString(decoded.ToArray());
        }

        // Not actually compressed (e.g. TestServer/HttpClient already transparently decoded it,
        // or compression didn't kick in) — that's fine, the content is already plain text.
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
