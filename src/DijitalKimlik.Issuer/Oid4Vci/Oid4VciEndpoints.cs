using System.Text.Json;
using System.Text.Json.Nodes;

using DijitalKimlik.Application.Offers;
using DijitalKimlik.Crypto.Jose;
using DijitalKimlik.Domain.Credentials;
using DijitalKimlik.Domain.Offers;

using Microsoft.Extensions.Options;

namespace DijitalKimlik.Issuer.Oid4Vci;

/// <summary>
/// OpenID4VCI 1.0 uç noktaları. Cüzdan bunları bu sırayla çağırır:
/// metadata → <c>/token</c> (pre-authorized code + PIN) → <c>/nonce</c> → <c>/credential</c> (kanıt JWT).
/// Doğrulayıcı ise <c>/.well-known/jwt-vc-issuer</c> (açık anahtar) ve <c>/status-lists/{id}</c> kullanır.
/// </summary>
public static class Oid4VciEndpoints
{
    public static IEndpointRouteBuilder MapOid4Vci(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/openid-credential-issuer", CredentialIssuerMetadata);
        app.MapGet("/.well-known/oauth-authorization-server", AuthorizationServerMetadata);
        app.MapGet("/.well-known/jwt-vc-issuer", JwtVcIssuerMetadata);
        app.MapPost("/token", Token);
        app.MapPost("/nonce", Nonce);
        app.MapPost("/credential", Credential);
        app.MapGet("/status-lists/{id:int}", StatusListToken);
        return app;
    }

    private static IResult CredentialIssuerMetadata(IOptions<IssuerOptions> options)
    {
        var id = options.Value.Identifier;
        var metadata = new JsonObject
        {
            ["credential_issuer"] = id,
            ["credential_endpoint"] = id + "/credential",
            ["nonce_endpoint"] = id + "/nonce",
            ["display"] = new JsonArray(new JsonObject { ["name"] = "Demo Göç Kurumu", ["locale"] = "tr-TR" }),
            ["credential_configurations_supported"] = new JsonObject
            {
                [CredentialIssuanceService.ConfigurationId] = new JsonObject
                {
                    ["format"] = IssuedCredential.SdJwtVcFormat,
                    ["vct"] = CredentialIssuanceService.Vct,
                    ["cryptographic_binding_methods_supported"] = new JsonArray("jwk"),
                    ["credential_signing_alg_values_supported"] = new JsonArray(Jws.Es256),
                    ["proof_types_supported"] = new JsonObject
                    {
                        ["jwt"] = new JsonObject { ["proof_signing_alg_values_supported"] = new JsonArray(Jws.Es256) },
                    },
                    ["credential_metadata"] = new JsonObject
                    {
                        ["display"] = new JsonArray(new JsonObject
                        {
                            ["name"] = "İkamet İzni",
                            ["locale"] = "tr-TR",
                            ["background_color"] = "#0b3d5c",
                            ["text_color"] = "#ffffff",
                        }),
                        ["claims"] = new JsonArray([.. ClaimLabels.Select(c => (JsonNode)new JsonObject
                        {
                            ["path"] = new JsonArray(c.Key),
                            ["display"] = new JsonArray(new JsonObject { ["name"] = c.Value, ["locale"] = "tr-TR" }),
                        })]),
                    },
                },
            },
        };
        return Json(metadata);
    }

    /// <summary>Cüzdanın kimlik bilgisindeki alanları ekranda gösterirken kullandığı Türkçe adlar.</summary>
    public static IReadOnlyDictionary<string, string> ClaimLabels { get; } = new Dictionary<string, string>
    {
        ["given_name"] = "Ad",
        ["family_name"] = "Soyad",
        ["birthdate"] = "Doğum tarihi",
        ["nationality"] = "Uyruk",
        ["identity_number"] = "Yabancı kimlik no",
        ["permit_number"] = "İzin no",
        ["permit_type"] = "İzin türü",
        ["valid_from"] = "Geçerlilik başlangıcı",
        ["valid_until"] = "Geçerlilik bitişi",
        ["age_over_18"] = "18 yaşından büyük",
    };

    /// <summary>
    /// <c>authorization_servers</c> verilmediği için düzenleyici kendi yetkilendirme sunucusudur;
    /// cüzdan token adresini buradan öğrenir (RFC 8414).
    /// </summary>
    private static IResult AuthorizationServerMetadata(IOptions<IssuerOptions> options)
    {
        var id = options.Value.Identifier;
        return Json(new JsonObject
        {
            ["issuer"] = id,
            ["token_endpoint"] = id + "/token",
            ["grant_types_supported"] = new JsonArray(OfferService.PreAuthorizedCodeGrant),
            ["pre-authorized_grant_anonymous_access_supported"] = true,
        });
    }

    private static IResult JwtVcIssuerMetadata(IOptions<IssuerOptions> options, IssuerSigningKey key) =>
        Json(new JsonObject
        {
            ["issuer"] = options.Value.Identifier,
            ["jwks"] = new JsonObject { ["keys"] = new JsonArray(key.PublicJwk) },
        });

    /// <summary>
    /// Token isteği bir HTML formu değil, cüzdanın gönderdiği <c>application/x-www-form-urlencoded</c>
    /// gövdedir. Form elle okunur; antiforgery burada anlamsızdır, çünkü çerez ya da oturum yoktur.
    /// </summary>
    private static async Task<IResult> Token(HttpContext context, IOfferRedeemer redeemer, AccessTokenService tokens)
    {
        NoStore(context);
        if (!context.Request.HasFormContentType)
        {
            return OAuthError("invalid_request", "Gövde application/x-www-form-urlencoded olmalı.");
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (form["grant_type"] != OfferService.PreAuthorizedCodeGrant)
        {
            return OAuthError("unsupported_grant_type", "Yalnız pre-authorized_code destekleniyor.");
        }

        var code = form["pre-authorized_code"].ToString();
        var txCode = form["tx_code"].ToString();
        if (code.Length == 0 || txCode.Length == 0)
        {
            return OAuthError("invalid_request", "pre-authorized_code ve tx_code zorunlu.");
        }

        var redemption = await redeemer.RedeemAsync(code, txCode, context.RequestAborted);

        // RFC 6749 §5.2: kod ya da PIN hatası invalid_grant'tir. Açıklama hangisinin yanlış olduğunu
        // söylemez; yalnız kalan deneme hakkı yoksa bunu bildirir.
        return redemption?.Result switch
        {
            RedeemResult.Success => Json(new JsonObject
            {
                ["access_token"] = tokens.Issue(redemption.ResidencePermitId, redemption.OfferId),
                ["token_type"] = "Bearer",
                ["expires_in"] = (long)tokens.Lifetime.TotalSeconds,
            }),
            RedeemResult.Locked => OAuthError("invalid_grant", "Çok fazla hatalı PIN: teklif kilitlendi."),
            _ => OAuthError("invalid_grant", "Teklif geçersiz, süresi dolmuş, kullanılmış ya da PIN yanlış."),
        };
    }

    private static IResult Nonce(HttpContext context, NonceStore nonces)
    {
        NoStore(context);
        return Json(new JsonObject { ["c_nonce"] = nonces.Issue() });
    }

    private static async Task<IResult> Credential(
        HttpContext context,
        AccessTokenService tokens,
        ProofValidator proofs,
        CredentialIssuanceService issuance,
        ILoggerFactory loggers)
    {
        NoStore(context);
        var authorization = context.Request.Headers.Authorization.ToString();
        var grant = authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            ? tokens.Validate(authorization["Bearer ".Length..].Trim())
            : null;
        if (grant is null)
        {
            // RFC 6750 §3: token yoksa ya da geçersizse 401 ve WWW-Authenticate başlığı.
            context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return Results.Unauthorized();
        }

        try
        {
            var request = await ReadJsonObjectAsync(context);
            if (JoseJson.GetString(request, "credential_configuration_id") != CredentialIssuanceService.ConfigurationId)
            {
                throw new Oid4VciException("unknown_credential_configuration", "Bu düzenleyici yalnız DemoResidencePermit verir.");
            }

            // Toplu verme (batch) metadata'da ilan edilmediği için tam bir kanıt beklenir.
            if (request["proofs"]?["jwt"] is not JsonArray { Count: 1 } jwtProofs
                || jwtProofs[0] is not JsonValue proofValue
                || !proofValue.TryGetValue<string>(out var proofJwt))
            {
                throw new Oid4VciException("invalid_proof", "proofs.jwt tek elemanlı bir dizi olmalı.");
            }

            var holder = proofs.Validate(proofJwt);
            var credential = await issuance.IssueAsync(grant, holder, context.RequestAborted);
            return Json(new JsonObject
            {
                ["credentials"] = new JsonArray(new JsonObject { ["credential"] = credential }),
            });
        }
        catch (Oid4VciException ex)
        {
            loggers.CreateLogger(typeof(Oid4VciEndpoints)).LogWarning("Credential isteği reddedildi: {Error}", ex.Error);
            return OAuthError(ex.Error, ex.Message);
        }
    }

    private static async Task<IResult> StatusListToken(int id, StatusListService statusLists, HttpContext context)
    {
        var token = await statusLists.BuildTokenAsync(id, context.RequestAborted);
        if (token is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = $"max-age={(int)StatusListService.TimeToLive.TotalSeconds}";
        return Results.Text(token, StatusListService.MediaType);
    }

    private static async Task<JsonObject> ReadJsonObjectAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType())
        {
            throw new Oid4VciException("invalid_credential_request", "Gövde application/json olmalı.");
        }

        try
        {
            return await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted) as JsonObject
                ?? throw new Oid4VciException("invalid_credential_request", "Gövde bir JSON nesnesi olmalı.");
        }
        catch (JsonException)
        {
            throw new Oid4VciException("invalid_credential_request", "Gövde geçerli JSON değil.");
        }
    }

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    private static IResult Json(JsonObject body) => Results.Text(JoseJson.Serialize(body), "application/json");

    private static IResult OAuthError(string error, string description) =>
        Results.Text(
            JoseJson.Serialize(new JsonObject { ["error"] = error, ["error_description"] = description }),
            "application/json",
            statusCode: StatusCodes.Status400BadRequest);
}
