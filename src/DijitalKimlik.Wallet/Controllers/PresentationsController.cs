using DijitalKimlik.Wallet.Credentials;
using DijitalKimlik.Wallet.Models;
using DijitalKimlik.Wallet.Presentations;

using Microsoft.AspNetCore.Mvc;

namespace DijitalKimlik.Wallet.Controllers;

/// <summary>Doğrulayıcının isteği: onay ekranı, sonra paylaş ya da reddet.</summary>
public class PresentationsController(PresentationClient presentations, WalletStore store) : Controller
{
    /// <summary>Aynı cihaz akışında doğrulayıcının bağlantısı buraya gelir (mobilde <c>openid4vp://</c>).</summary>
    [HttpGet("/authorize")]
    public IActionResult Authorize()
    {
        var walletId = WalletSession.GetOrCreateWalletId(HttpContext);
        var raw = Request.QueryString.Value ?? string.Empty;
        try
        {
            var request = PresentationRequest.Parse(raw);
            var matches = store.List(walletId).Where(c => PresentationClient.Matches(c, request.Credential)).ToList();
            return View("Consent", new ConsentViewModel(request, raw, matches));
        }
        catch (WalletException ex)
        {
            return View("Problem", ex.Message);
        }
    }

    [HttpPost("/authorize/respond")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Respond(string request, string? credentialId, string decision, CancellationToken cancellationToken)
    {
        var walletId = WalletSession.GetOrCreateWalletId(HttpContext);
        try
        {
            var parsed = PresentationRequest.Parse(request);
            Uri? redirect;
            if (decision == "share")
            {
                // Kimlik bilgisi yalnız bu cüzdanın deposunda aranır: başka cüzdanın kaydı seçilemez.
                var credential = (credentialId is null ? null : store.Find(walletId, credentialId))
                    ?? throw new WalletException("Seçilen kimlik bilgisi cüzdanda yok.");
                redirect = await presentations.RespondAsync(parsed, credential, cancellationToken);
            }
            else
            {
                redirect = await presentations.DeclineAsync(parsed, cancellationToken);
            }

            if (redirect is not null)
            {
                return Redirect(redirect.AbsoluteUri);
            }

            TempData["Message"] = decision == "share" ? "Bilgiler paylaşıldı." : "Paylaşım reddedildi.";
            return RedirectToAction("Index", "Home");
        }
        catch (Exception ex) when (ex is WalletException or HttpRequestException or TaskCanceledException)
        {
            return View("Problem", OffersController.Describe(ex));
        }
    }
}
