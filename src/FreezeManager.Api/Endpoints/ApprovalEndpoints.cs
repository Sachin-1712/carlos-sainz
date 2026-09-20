using FreezeManager.Api.Contracts;
using FreezeManager.Domain.Approvals;
using FreezeManager.Infrastructure.Changes;
using FreezeManager.Infrastructure.Overrides;

namespace FreezeManager.Api.Endpoints;

public static class ApprovalEndpoints
{
    public static RouteGroupBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/changes").WithTags("Approvals");

        group.MapGet("/{reference}/approvals", async (
            string reference,
            ChangeRequestService service,
            CancellationToken cancellationToken) =>
        {
            var change = await service.GetAsync(reference, cancellationToken);

            if (change is null)
            {
                return Results.NotFound();
            }

            var requirement = await service.RequirementForAsync(change, cancellationToken);
            return Results.Ok(ApprovalChainResponse.From(requirement, change.Approvals));
        })
        .WithSummary("The approval chain this change needs, and who has decided so far.");

        group.MapPost("/{reference}/approvals", async (
            string reference,
            ApprovalInput input,
            ChangeRequestService service,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(input.Approver))
            {
                return Results.BadRequest(new { errors = new[] { "An approver is required." } });
            }

            var result = await service.RecordApprovalAsync(
                reference, input.Role, input.Approver, input.Decision, input.Comment, cancellationToken);

            return result.Status switch
            {
                ChangeOperationStatus.Succeeded => Results.Ok(ChangeResponse.From(result.Change!)),
                ChangeOperationStatus.NotFound => Results.NotFound(),
                _ => Results.UnprocessableEntity(new { errors = result.Problems })
            };
        })
        .WithSummary("Record one role's decision. The change advances when the chain is complete.");

        return group;
    }
}

public static class OverrideEndpoints
{
    public static RouteGroupBuilder MapOverrideEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/changes").WithTags("Overrides");

        group.MapGet("/{reference}/override", async (
            string reference,
            OverrideService service,
            CancellationToken cancellationToken) =>
        {
            var overrides = await service.ListAsync(reference, cancellationToken);

            return overrides.Count == 0
                ? Results.NotFound()
                : Results.Ok(overrides.Select(o => OverrideResponse.From(o)).ToArray());
        })
        .WithSummary("Overrides raised against this change, newest first.");

        group.MapPost("/{reference}/override", async (
            string reference,
            OverrideRequestInput input,
            OverrideService service,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(input.RequestedBy))
            {
                return Results.BadRequest(new { errors = new[] { "A requester is required." } });
            }

            var duration = input.GrantDurationHours is > 0
                ? TimeSpan.FromHours(input.GrantDurationHours.Value)
                : (TimeSpan?)null;

            var result = await service.RequestAsync(
                reference, input.IncidentReference ?? string.Empty, input.Justification ?? string.Empty,
                input.RequestedBy, duration, input.BreakGlass, cancellationToken);

            return Respond(result, StatusCodes.Status201Created);
        })
        .WithSummary("Raise an emergency override. Needs a linked incident and a written justification.");

        group.MapPost("/{reference}/override/approvals", async (
            string reference,
            ApprovalInput input,
            OverrideService service,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(input.Approver))
            {
                return Results.BadRequest(new { errors = new[] { "An approver is required." } });
            }

            var result = await service.ApproveAsync(
                reference, input.Role, input.Approver, input.Decision, input.Comment, cancellationToken);

            return Respond(result, StatusCodes.Status200OK);
        })
        .WithSummary("Approve or reject an override. A grant starts its clock when the chain completes.");

        group.MapPost("/{reference}/override/revoke", async (
            string reference,
            ApprovalInput? input,
            OverrideService service,
            CancellationToken cancellationToken) =>
        {
            var actor = string.IsNullOrWhiteSpace(input?.Approver) ? "unknown" : input!.Approver!;
            return Respond(await service.RevokeAsync(reference, actor, cancellationToken), StatusCodes.Status200OK);
        })
        .WithSummary("Withdraw an override before it expires.")
        .Accepts<ApprovalInput>(isOptional: true, contentType: "application/json");

        group.MapPost("/{reference}/override/retrospective", async (
            string reference,
            RetrospectiveInput input,
            OverrideService service,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(input.CompletedBy))
            {
                return Results.BadRequest(new { errors = new[] { "A completer is required." } });
            }

            var result = await service.CompleteRetrospectiveAsync(
                reference, input.CompletedBy, input.Notes ?? string.Empty, cancellationToken);

            return Respond(result, StatusCodes.Status200OK);
        })
        .WithSummary("Complete the retrospective a break-glass override owes within 24 hours.");

        return group;
    }

    private static IResult Respond(OverrideOperationResult result, int successStatus) => result.Status switch
    {
        OverrideOperationStatus.Succeeded => Results.Json(
            OverrideResponse.From(result.Override!, result.OutstandingRoles),
            statusCode: successStatus),

        OverrideOperationStatus.NotFound => Results.NotFound(new { errors = result.Problems }),

        OverrideOperationStatus.NotApplicable => Results.Conflict(new { errors = result.Problems }),

        _ => Results.UnprocessableEntity(new { errors = result.Problems })
    };
}
