using FluentValidation;
using RfidSyncApi.Application.DTOs;

namespace RfidSyncApi.Application.Validators;

/// <summary>
/// FluentValidation rules for the POST /api/sync payload.
///
/// <para>
/// <b>operation</b> field rules:
///   • Optional — when absent or null the server defaults to INSERT.
///   • When provided, must be one of: INSERT, UPDATE, DELETE (case-insensitive).
/// </para>
///
/// All scan payload fields (tag_id, event_type, …) are validated directly
/// on ChangeRecord — there is no nested "data" object.
/// </summary>
public class SyncRequestValidator : AbstractValidator<SyncRequest>
{
    private const int MaxBatchSize = 10_000;

    // RFID tags: 1–64 alphanumeric / hyphen / underscore characters.
    private static readonly System.Text.RegularExpressions.Regex TagIdRegex =
        new(@"^[A-Za-z0-9\-_]{1,64}$",
            System.Text.RegularExpressions.RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));

    private static readonly HashSet<string> ValidOperations =
        new(StringComparer.OrdinalIgnoreCase) { "INSERT", "UPDATE", "DELETE" };

    private static readonly HashSet<string> ValidEventTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CHECK_IN", "CHECK_OUT", "SCAN", "INSPECTION", "MAINTENANCE", "AUDIT", "TRANSFER"
        };

    public SyncRequestValidator()
    {
        // ── Top-level ──────────────────────────────────────────────────────────
        RuleFor(x => x.DeviceId)
            .NotEmpty().WithMessage("device_id is required.")
            .MaximumLength(128);

        RuleFor(x => x.LastSyncTime)
            .GreaterThanOrEqualTo(0).WithMessage("last_sync_time must be >= 0.");

        RuleFor(x => x.Changes)
            .NotNull().WithMessage("changes array is required.")
            .Must(c => c.Count <= MaxBatchSize)
            .WithMessage($"Batch size exceeds maximum of {MaxBatchSize} records.");

        // ── Per-record ─────────────────────────────────────────────────────────
        RuleForEach(x => x.Changes).ChildRules(change =>
        {
            change.RuleFor(c => c.LocalId)
                  .NotEmpty().WithMessage("local_id is required.");

            // operation is optional; when supplied it must be a recognised value
            change.When(c => !string.IsNullOrWhiteSpace(c.Operation), () =>
            {
                change.RuleFor(c => c.Operation)
                      .Must(op => ValidOperations.Contains(op!))
                      .WithMessage("operation must be INSERT, UPDATE, or DELETE.");
            });

            // ── INSERT / UPDATE: require scan payload fields ──────────────────
            change.When(c => c.EffectiveOperation == "INSERT" || c.EffectiveOperation == "UPDATE",
            () =>
            {
                change.RuleFor(c => c.TagId)
                      .NotEmpty()
                      .Matches(TagIdRegex)
                      .WithMessage("tag_id must be 1–64 alphanumeric/hyphen/underscore characters.");

                change.RuleFor(c => c.EventType)
                      .NotEmpty()
                      .Must(et => ValidEventTypes.Contains(et))
                      .WithMessage($"event_type must be one of: {string.Join(", ", ValidEventTypes)}.");

                change.RuleFor(c => c.CreatedAt)
                      .GreaterThan(0).WithMessage("created_at (epoch ms) must be > 0.");

                change.RuleFor(c => c.UpdatedAt)
                      .GreaterThan(0).WithMessage("updated_at (epoch ms) must be > 0.");

                change.RuleFor(c => c.Version)
                      .GreaterThan(0).WithMessage("version must be >= 1.");
            });

            // ── UPDATE / DELETE: require server_id ────────────────────────────
            change.When(c => c.EffectiveOperation == "UPDATE" || c.EffectiveOperation == "DELETE",
            () =>
            {
                change.RuleFor(c => c.ServerId)
                      .NotNull()
                      .WithMessage("server_id is required for UPDATE/DELETE operations.");
            });
        });
    }
}
