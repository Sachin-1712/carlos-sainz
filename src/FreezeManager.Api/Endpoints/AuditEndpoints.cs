using FreezeManager.Api.Contracts;
using FreezeManager.Infrastructure.Audit;
using FreezeManager.Infrastructure.Overrides;

namespace FreezeManager.Api.Endpoints;

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit").WithTags("Audit");

        group.MapGet("/", async (
            string? subject,
            int? limit,
            AuditReader reader,
            CancellationToken cancellationToken) =>
        {
            var entries = await reader.ReadAsync(subject, limit ?? 200, cancellationToken);
            return Results.Ok(entries.Select(AuditEntryResponse.From).ToArray());
        })
        .WithSummary("The audit log in sequence order, optionally for one subject.");

        group.MapGet("/verify", async (AuditReader reader, CancellationToken cancellationToken) =>
        {
            var verification = await reader.VerifyAsync(cancellationToken);
            var response = AuditVerificationResponse.From(verification);

            // A broken chain is not a server fault, so it is not a 500. It is a finding, and the
            // caller needs the body either way.
            return Results.Json(
                response,
                statusCode: verification.IsValid ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);
        })
        .WithSummary("Walk the hash chain and report whether the log has been altered.");

        group.MapPost("/sweep-overrides", async (
            OverrideExpirySweeper sweeper,
            CancellationToken cancellationToken) =>
        {
            var result = await sweeper.SweepAsync(cancellationToken);
            return Results.Ok(new { result.Expired, result.RetrospectivesOverdue });
        })
        .WithSummary("Run the override expiry sweep now. Also runs on a timer.");

        return group;
    }
}
