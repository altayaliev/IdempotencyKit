using Microsoft.Extensions.Options;

namespace Idempo;

/// <summary>
/// Catches an obviously-broken <see cref="IdempotencyOptions"/> configuration at startup
/// (via <c>options.ValidateOnStart()</c>-style validation) instead of it surfacing later as
/// confusing runtime behavior. Registered automatically by <c>AddIdempo()</c>.
/// </summary>
public sealed class IdempotencyOptionsValidator : IValidateOptions<IdempotencyOptions>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.HeaderName))
        {
            failures.Add($"{nameof(IdempotencyOptions.HeaderName)} must not be empty.");
        }

        if (options.Methods.Count == 0)
        {
            failures.Add($"{nameof(IdempotencyOptions.Methods)} must not be empty.");
        }

        if (options.PendingTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(IdempotencyOptions.PendingTimeout)} must be positive.");
        }

        if (options.CompletedTtl <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(IdempotencyOptions.CompletedTtl)} must be positive.");
        }

        if (options.CompletedTtl < options.PendingTimeout)
        {
            failures.Add(
                $"{nameof(IdempotencyOptions.CompletedTtl)} ({options.CompletedTtl}) must be at least " +
                $"{nameof(IdempotencyOptions.PendingTimeout)} ({options.PendingTimeout}) — otherwise a completed " +
                "result could expire before a slow caller's own request even finishes.");
        }

        if (options.MaxKeyLength <= 0)
        {
            failures.Add($"{nameof(IdempotencyOptions.MaxKeyLength)} must be positive.");
        }

        if (options.MaxRequestBodyBytes <= 0)
        {
            failures.Add($"{nameof(IdempotencyOptions.MaxRequestBodyBytes)} must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
