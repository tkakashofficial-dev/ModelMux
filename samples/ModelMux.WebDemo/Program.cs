using Microsoft.Extensions.AI;
using ModelMux;
using ModelMux.Cost;
using ModelMux.Cost.Attribution;
using ModelMux.WebDemo.Reporting;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// The entire AI setup. Which vendor serves which profile lives in
// appsettings.json; nothing below this block names a provider.
// ---------------------------------------------------------------------------
builder.Services
    .AddModelMux(builder.Configuration)
    .AddCostTracking();

builder.Services.AddSingleton<EmployeeRepository>();
builder.Services.AddScoped<ReportQueryService>();

var app = builder.Build();

// ---------------------------------------------------------------------------
// Which profile maps to which provider right now.
// ---------------------------------------------------------------------------
app.MapGet("/", (IModelMux mux) => Results.Ok(new
{
    service = "ModelMux web demo",
    defaultProfile = mux.DefaultProfileName,
    profiles = mux.ProfileNames.Select(name =>
    {
        var profile = mux.GetProfile(name);
        var caps = mux.GetCapabilities(name);

        return new
        {
            name,
            provider = profile.Provider,
            model = profile.Model,
            endpoint = profile.Endpoint ?? "(provider default)",
            profile.Description,
            capabilities = new
            {
                caps.Streaming,
                caps.ToolCalling,
                caps.StructuredOutput,
                caps.Vision,
            },
        };
    }),
    endpoints = new[]
    {
        "POST /api/chat            { \"message\": \"...\", \"profile\": \"fast\" }",
        "POST /api/chat/stream     { \"message\": \"...\" }  (server-sent events)",
        "POST /api/reports/query   { \"question\": \"...\" }",
        "GET  /api/usage           cost and token totals",
    },
}));

// ---------------------------------------------------------------------------
// Section 20: provider-neutral chat. This handler never names a vendor.
// ---------------------------------------------------------------------------
app.MapPost("/api/chat", async (
    ChatRequest request,
    IModelMux mux,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "message is required." });
    }

    using var scope = UsageScope.Begin(feature: "chat");

    var response = await mux.GetClient(request.Profile).GetResponseAsync(
        [new ChatMessage(ChatRole.User, request.Message)],
        cancellationToken: cancellationToken);

    return Results.Ok(new
    {
        reply = response.Text,
        model = response.ModelId,
        profile = request.Profile ?? mux.DefaultProfileName,
    });
});

// ---------------------------------------------------------------------------
// Section 15: streaming, over the same profile indirection.
// ---------------------------------------------------------------------------
app.MapPost("/api/chat/stream", async (
    ChatRequest request,
    IModelMux mux,
    HttpResponse response,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        response.StatusCode = StatusCodes.Status400BadRequest;
        await response.WriteAsJsonAsync(new { error = "message is required." }, cancellationToken);
        return;
    }

    response.ContentType = "text/event-stream";

    using var scope = UsageScope.Begin(feature: "chat-stream");

    await foreach (var update in mux.GetStreamingResponseAsync(
        request.Message, request.Profile, cancellationToken))
    {
        if (!string.IsNullOrEmpty(update.Text))
        {
            await response.WriteAsync($"data: {update.Text}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }

    await response.WriteAsync("data: [DONE]\n\n", cancellationToken);
});

// ---------------------------------------------------------------------------
// Section 21: natural language -> validated intent -> safe query.
// The model proposes; the application disposes.
// ---------------------------------------------------------------------------
app.MapPost("/api/reports/query", async (
    ReportRequest request,
    ReportQueryService reports,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "question is required." });
    }

    using var scope = UsageScope.Begin(feature: "report-query");

    var result = await reports.AskAsync(request.Question, cancellationToken);

    // The intent is returned either way, so a human can see exactly what the model
    // proposed and why it was accepted or rejected.
    return result.Succeeded
        ? Results.Ok(new { result.Question, result.Intent, count = result.Rows.Count, rows = result.Rows })
        : Results.BadRequest(new { result.Question, result.Intent, errors = result.ValidationErrors });
});

// The catalog the model is constrained to, exposed so the limits are inspectable.
app.MapGet("/api/reports/schema", () => Results.Ok(new
{
    reports = ReportCatalog.All.Values,
    operators = FilterOperator.All,
}));

// ---------------------------------------------------------------------------
// Cost and token totals recorded by ModelMux.Cost.
// ---------------------------------------------------------------------------
app.MapGet("/api/usage", async (IUsageQuery usage, CancellationToken cancellationToken) =>
{
    var summary = await usage.SummarizeAsync(new UsageFilter(), cancellationToken);
    var recent = await usage.QueryAsync(new UsageFilter { Limit = 20 }, cancellationToken);

    return Results.Ok(new
    {
        summary.RequestCount,
        summary.SuccessCount,
        summary.FailureCount,
        summary.TotalTokens,
        summary.Cost,
        summary.Currency,
        summary.UnpricedCount,
        summary.AverageDurationMs,
        recent = recent.Select(r => new
        {
            r.TimestampUtc,
            r.ModelId,
            r.Feature,
            r.TotalTokens,
            r.Cost,
            r.DurationMs,
            r.Success,
        }),
    });
});

// ---------------------------------------------------------------------------
// Turn a provider failure into a sensible HTTP status, using the vendor-neutral
// category rather than catching an OpenAI or Gemini exception type.
// ---------------------------------------------------------------------------
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features
        .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;

    var (status, body) = error switch
    {
        ModelMuxConfigurationException configError =>
            (StatusCodes.Status500InternalServerError,
             new { error = "ModelMux is misconfigured.", detail = configError.Message, retryable = false }),

        UnsupportedCapabilityException capability =>
            (StatusCodes.Status400BadRequest,
             new { error = "The selected profile cannot do that.", detail = capability.Message, retryable = false }),

        ModelMuxProviderException provider => (
            provider.Category switch
            {
                AiErrorCategory.RateLimit => StatusCodes.Status429TooManyRequests,
                AiErrorCategory.AuthenticationFailure => StatusCodes.Status500InternalServerError,
                AiErrorCategory.Timeout => StatusCodes.Status504GatewayTimeout,
                AiErrorCategory.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
                AiErrorCategory.InvalidRequest => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status502BadGateway,
            },
            new
            {
                error = $"The AI provider failed: {provider.Category}.",
                detail = provider.Message,
                retryable = provider.IsRetryable,
            }),

        _ => (StatusCodes.Status500InternalServerError,
              new { error = "Unexpected error.", detail = error?.Message ?? "unknown", retryable = false }),
    };

    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(body);
}));

app.Run();

/// <summary>Request body for the chat endpoints.</summary>
/// <param name="Message">What to ask the model.</param>
/// <param name="Profile">Profile to route through, or null for the default.</param>
internal sealed record ChatRequest(string Message, string? Profile);

/// <summary>Request body for the reporting endpoint.</summary>
/// <param name="Question">A natural-language question about the employee report.</param>
internal sealed record ReportRequest(string Question);

/// <summary>Exposed so the demo can be referenced from tests.</summary>
public partial class Program;
