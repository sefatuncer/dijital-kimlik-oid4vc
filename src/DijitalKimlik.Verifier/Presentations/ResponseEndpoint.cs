using System.Text.Json.Nodes;

using DijitalKimlik.Crypto.Jose;

using Microsoft.Extensions.Options;

namespace DijitalKimlik.Verifier.Presentations;

/// <summary>
/// OpenID4VP <c>direct_post</c> yanıt adresi: cüzdan sunumu tarayıcı üzerinden değil, doğrudan buraya
/// form olarak gönderir. Yanıt, cüzdanın tarayıcıyı göndereceği <c>redirect_uri</c>'yi taşır (§8.2).
/// </summary>
public static class ResponseEndpoint
{
    public static IEndpointRouteBuilder MapPresentationResponse(this IEndpointRouteBuilder app)
    {
        app.MapPost("/presentations/response", HandleAsync);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        TransactionStore transactions,
        PresentationVerifier verifier,
        IOptions<VerifierOptions> options,
        ILoggerFactory loggers)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.HasFormContentType)
        {
            return Error("invalid_request", "Gövde application/x-www-form-urlencoded olmalı.");
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var transaction = transactions.FindByState(form["state"].ToString());
        if (transaction is null)
        {
            return Error("invalid_request", "state bilinmiyor ya da süresi dolmuş.");
        }

        var result = form.ContainsKey("error")
            ? VerificationResult.Rejected("Kullanıcı paylaşmayı reddetti.")
            : await verifier.VerifyAsync(transaction, form["vp_token"].FirstOrDefault(), context.RequestAborted);

        if (!transaction.TryComplete(result))
        {
            // Aynı state ile ikinci yanıt: ilki sayılır, bu reddedilir (replay).
            return Error("invalid_request", "Bu isteğe zaten yanıt verildi.");
        }

        // Günlükte kişisel veri yok: yalnız oturum kimliği, senaryo ve sonuç.
        loggers.CreateLogger(typeof(ResponseEndpoint)).LogInformation(
            "Sunum {TransactionId} ({Scenario}): {Outcome}",
            transaction.Id,
            transaction.Scenario.Key,
            result.Success ? "doğrulandı" : result.Checks[^1].Name);

        var redirect = $"{options.Value.Identifier.TrimEnd('/')}/presentations/{transaction.Id}/result?response_code={transaction.ResponseCode}";
        return Results.Text(JoseJson.Serialize(new JsonObject { ["redirect_uri"] = redirect }), "application/json");
    }

    private static IResult Error(string error, string description) =>
        Results.Text(
            JoseJson.Serialize(new JsonObject { ["error"] = error, ["error_description"] = description }),
            "application/json",
            statusCode: StatusCodes.Status400BadRequest);
}
