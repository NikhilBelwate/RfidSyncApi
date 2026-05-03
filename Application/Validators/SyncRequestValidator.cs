using FluentValidation;
using RfidSyncApi.Application.DTOs;

namespace RfidSyncApi.Application.Validators;

/// <summary>
/// FluentValidation rules for the POST /api/sync payload.
/// Executes before any service logic, keeping services clean of validation noise.
/// </summary>
public class SyncRequestValidator : AbstractValidator<SyncRequest>
{
    private const int MaxBatchSize = 10_000;

    // RFID tags must be 1–64 alphanumeric/hyphen/underscore chars.
    private static readonly System.Text.RegularExpressions.Regex TagIdRegex =
        new(@"^[A-Za-z0-9\-_]{1,64}$",
            System.Text.RegularExpressions.RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));

    private static readonly HashSet<string> ValidOperations =
        new(StringComparer.OrdinalIgnoreCase) { "INSERT", "UPDATE", "DELETE" };

    private static readonly HashSet<string> ValidEventTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CHECK_IN", "CHECK_OUT", "INSPECTION", "MAINTENANCE", "AUDIT", "TRANSFER"
        };

    public SyncRequestValidator()
    {
        RuleFor(x => x.DeviceId)
            .NotEmpty().WithMessage("device_id is required.")
            .MaximumLength(128);

        RuleFor(x => x.Changes)
            .NotNull().WithMessage("changes array is required.")
            .Must(c => c.Count <= MaxBatchSize)
            .WithMessage($"Batch size exceeds maximum of {MaxBatchSize} records.");

        RuleForEach(x => x.Changes).ChildRules(change =>
        {
            change.RuleFor(c => c.LocalId)
                  .NotEmpty().WithMessage("local_id is required.");

            change.RuleFor(c => c.Operation)
                  .NotEmpty()
                  .Must(op => ValidOperations.Contains(op))
                  .WithMessage("operation must be INSERT, UPDATE, or DELETE.");

            // Data is required for INSERT/UPDATE
            change.When(c => string.Equals(c.Operation, "INSERT", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(c.Operation, "UPDATE", StringComparison.OrdinalIgnoreCase),
            () =>
            {
                change.RuleFor(c => c.Data)
                      .NotNull().WithMessage("data is required for INSERT/UPDATE operations.");

                change.RuleFor(c => c.Data!.TagId)
                      .NotEmpty()
                      .Matches(TagIdRegex)
                      .WithMessage("tag_id must be 1–64 alphanumeric/hyphen/underscore characters.");

                change.RuleFor(c => c.Data!.UserId)
                      .NotEmpty().MaximumLength(128);

                change.RuleFor(c => c.Data!.SiteId)
                      .NotEmpty().MaximumLength(128);

                change.RuleFor(c => c.Data!.EventType)
                      .NotEmpty()
                      .Must(et => ValidEventTypes.Contains(et))
                      .WithMessage($"event_type must be one of: {string.Join(", ", ValidEventTypes)}.");

                change.RuleFor(c => c.Data!.Version)
                      .GreaterThan(0).WithMessage("version must be > 0.");
            });

            // UPDATE and DELETE require a server_id
            change.When(c => string.Equals(c.Operation, "UPDATE", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(c.Operation, "DELETE", StringComparison.OrdinalIgnoreCase),
            () =>
            {
                change.RuleFor(c => c.ServerId)
                      .NotNull()
                      .WithMessage("server_id is required for UPDATE/DELETE operations.");
            });
        });
    }
}
