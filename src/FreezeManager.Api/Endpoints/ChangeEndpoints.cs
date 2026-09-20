using FreezeManager.Api.Contracts;
using FreezeManager.Domain.Changes;
using FreezeManager.Infrastructure.Changes;

namespace FreezeManager.Api.Endpoints;

public static class ChangeEndpoints
{
    public static RouteGroupBuilder MapChangeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/changes").WithTags("Changes");

        group.MapGet("/", async (
            ChangeRequestService service,
            string? state,
            string? affectedService,
            CancellationToken cancellationToken) =>
        {
            ChangeState? parsed = null;

            if (!string.IsNullOrWhiteSpace(state))
            {
                if (!Enum.TryParse<ChangeState>(state, ignoreCase: true, out var value))
                {
                    return Results.BadRequest(new { error = $"Unknown state '{state}'.", allowed = Enum.GetNames<ChangeState>() });
                }

                parsed = value;
            }

            var changes = await service.ListAsync(parsed, affectedService, cancellationToken);
            return Results.Ok(changes.Select(c => ChangeResponse.From(c)).ToArray());
        })
        .WithSummary("List changes, optionally filtered by state or affected service.");

        group.MapGet("/{reference}", async (
            string reference,
            ChangeRequestService service,
            CancellationToken cancellationToken) =>
        {
            var change = await service.GetAsync(reference, cancellationToken);
            return change is null ? Results.NotFound() : Results.Ok(ChangeResponse.From(change));
        })
        .WithSummary("Fetch one change by reference.");

        group.MapPost("/", async (
            ChangeRequestInput input,
            ChangeRequestService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuild(input, out var request, out var problems))
            {
                return Results.BadRequest(new { errors = problems });
            }

            var result = await service.CreateDraftAsync(request, cancellationToken);

            return result.Succeeded
                ? Results.Created(
                    $"/api/changes/{result.Change!.Reference}",
                    ChangeResponse.From(result.Change))
                : Results.BadRequest(new { errors = result.Problems });
        })
        .WithSummary("Raise a change as a draft. A draft may be incomplete.");

        group.MapPut("/{reference}", async (
            string reference,
            ChangeRequestInput input,
            ChangeRequestService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuild(input, out var request, out var problems))
            {
                return Results.BadRequest(new { errors = problems });
            }

            var result = await service.UpdateDraftAsync(reference, request, cancellationToken);
            return Respond(result, reference, ChangeState.Draft, "update");
        })
        .WithSummary("Replace a draft. Only drafts can be edited.");

        group.MapDelete("/{reference}", async (
            string reference,
            ChangeRequestService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteDraftAsync(reference, cancellationToken);

            return result.Status switch
            {
                ChangeOperationStatus.Succeeded => Results.NoContent(),
                ChangeOperationStatus.NotFound => Results.NotFound(),
                _ => Results.Conflict(new { errors = result.Problems })
            };
        })
        .WithSummary("Delete a draft. Anything past draft must be cancelled, not deleted.");

        // ---- lifecycle transitions ----------------------------------------------------------

        MapTransition(group, "submit", ChangeState.Submitted, acceptsWindow: true,
            "Submit a change. Runs the freeze gate.");

        MapTransition(group, "schedule", ChangeState.Scheduled, acceptsWindow: true,
            "Confirm a change's window. Runs the freeze gate.");

        MapTransition(group, "withdraw", ChangeState.Draft, acceptsWindow: false,
            "Withdraw a submitted change back to draft for rework.");

        MapTransition(group, "start", ChangeState.Implementing, acceptsWindow: false,
            "Begin implementing a scheduled change.");

        MapTransition(group, "complete", ChangeState.Implemented, acceptsWindow: false,
            "Record that implementation finished.");

        MapTransition(group, "fail", ChangeState.Failed, acceptsWindow: false,
            "Record that implementation failed.");

        MapTransition(group, "roll-back", ChangeState.RolledBack, acceptsWindow: false,
            "Record that a failed change was backed out.");

        MapTransition(group, "close", ChangeState.Closed, acceptsWindow: false,
            "Close a change.");

        MapTransition(group, "cancel", ChangeState.Cancelled, acceptsWindow: false,
            "Withdraw a change before implementation.");

        return group;
    }

    private static void MapTransition(
        RouteGroupBuilder group,
        string verb,
        ChangeState target,
        bool acceptsWindow,
        string summary)
    {
        if (acceptsWindow)
        {
            // Submit and schedule take an optional replacement window, which is what makes accepting
            // the suggestion from a rejection a single call rather than an edit followed by a retry.
            group.MapPost($"/{{reference}}/{verb}", async (
                string reference,
                ChangeWindowInput? window,
                ChangeRequestService service,
                CancellationToken cancellationToken) =>
            {
                (DateTimeOffset, DateTimeOffset)? newWindow = null;

                if (window is { RequestedStartUtc: not null, RequestedEndUtc: not null })
                {
                    newWindow = (window.RequestedStartUtc.Value, window.RequestedEndUtc.Value);
                }

                var result = await service.TransitionAsync(reference, target, newWindow, cancellationToken);
                return Respond(result, reference, target, verb);
            })
            .WithSummary(summary)
            .Accepts<ChangeWindowInput>(isOptional: true, contentType: "application/json");

            return;
        }

        group.MapPost($"/{{reference}}/{verb}", async (
            string reference,
            ChangeRequestService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.TransitionAsync(reference, target, null, cancellationToken);
            return Respond(result, reference, target, verb);
        })
        .WithSummary(summary);
    }

    private static IResult Respond(
        ChangeOperationResult result,
        string reference,
        ChangeState target,
        string verb)
    {
        switch (result.Status)
        {
            case ChangeOperationStatus.Succeeded:
                return Results.Ok(ChangeResponse.From(result.Change!, result.GateDecision, result.UsedOverride));

            case ChangeOperationStatus.NotFound:
                return Results.NotFound();

            case ChangeOperationStatus.BlockedByFreeze:
                return Results.Json(
                    BlockedByFreezeProblem.From(
                        result.Change!,
                        target,
                        result.GateDecision!,
                        $"/api/changes/{reference}/{verb}"),
                    statusCode: StatusCodes.Status409Conflict,
                    contentType: "application/problem+json");

            case ChangeOperationStatus.IllegalTransition:
                return Results.Json(
                    new
                    {
                        type = "https://freeze-manager.invalid/problems/illegal-transition",
                        title = "Illegal lifecycle transition",
                        status = StatusCodes.Status409Conflict,
                        detail = result.Problems.FirstOrDefault(),
                        reference,
                        currentState = result.Change!.State.ToString(),
                        requestedState = target.ToString(),
                        allowedNextStates = result.Change.NextStates.Select(s => s.ToString()).ToArray()
                    },
                    statusCode: StatusCodes.Status409Conflict,
                    contentType: "application/problem+json");

            default:
                return Results.UnprocessableEntity(new { errors = result.Problems });
        }
    }

    private static bool TryBuild(
        ChangeRequestInput input,
        out NewChangeRequest request,
        out IReadOnlyList<string> problems)
    {
        var found = new List<string>();

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            found.Add("A title is required.");
        }

        if (string.IsNullOrWhiteSpace(input.RequestedBy))
        {
            found.Add("A requester is required.");
        }

        if (input.RequestedStartUtc is null || input.RequestedEndUtc is null)
        {
            found.Add("A requested start and end are required.");
        }
        else if (input.RequestedEndUtc <= input.RequestedStartUtc)
        {
            found.Add("A change window must end after it starts.");
        }

        if (found.Count > 0)
        {
            request = null!;
            problems = found;
            return false;
        }

        request = new NewChangeRequest
        {
            Title = input.Title!,
            RequestedBy = input.RequestedBy!,
            Description = input.Description,
            ImplementationPlan = input.ImplementationPlan,
            BackoutPlan = input.BackoutPlan,
            Type = input.Type,
            Impact = input.Impact,
            Likelihood = input.Likelihood,
            AffectedServiceKeys = input.AffectedServiceKeys ?? Array.Empty<string>(),
            RequestedStartUtc = input.RequestedStartUtc!.Value,
            RequestedEndUtc = input.RequestedEndUtc!.Value
        };

        problems = Array.Empty<string>();
        return true;
    }
}
