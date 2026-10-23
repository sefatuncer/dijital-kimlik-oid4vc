using DijitalKimlik.Wallet.Credentials;
using DijitalKimlik.Wallet.Models;

using Microsoft.AspNetCore.Mvc;

namespace DijitalKimlik.Wallet.Controllers;

/// <summary>Düzenleyicinin bağlantısıyla gelen teklif: önce göster, PIN girilince kimlik bilgisini al.</summary>
public class OffersController(IssuanceClient issuance, WalletStore store) : Controller
{
    [HttpGet("/offers/receive")]
    public async Task<IActionResult> Receive([FromQuery(Name = "credential_offer")] string? offer, CancellationToken cancellationToken)
    {
        WalletSession.GetOrCreateWalletId(HttpContext);
        if (string.IsNullOrEmpty(offer))
        {
            return View("Problem", "Bağlantıda kimlik bilgisi teklifi yok.");
        }

        try
        {
            return View(new ReceiveViewModel(offer, await issuance.PreviewAsync(offer, cancellationToken), Error: null));
        }
        catch (Exception ex) when (ex is WalletException or HttpRequestException or TaskCanceledException)
        {
            return View("Problem", Describe(ex));
        }
    }

    [HttpPost("/offers/accept")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(string offer, string? txCode, CancellationToken cancellationToken)
    {
        var walletId = WalletSession.GetOrCreateWalletId(HttpContext);
        try
        {
            var credential = await issuance.ReceiveAsync(offer, txCode, cancellationToken);
            store.Add(walletId, credential);
            TempData["Message"] = $"{credential.DisplayName} cüzdanınıza eklendi.";
            return RedirectToAction("Index", "Home");
        }
        catch (Exception ex) when (ex is WalletException or HttpRequestException or TaskCanceledException)
        {
            // PIN yanlışsa aynı ekran hata iletisiyle yeniden açılır; teklif kilitlenmediyse yeniden denenir.
            try
            {
                return View("Receive", new ReceiveViewModel(offer, await issuance.PreviewAsync(offer, cancellationToken), Describe(ex)));
            }
            catch (Exception inner) when (inner is WalletException or HttpRequestException or TaskCanceledException)
            {
                return View("Problem", Describe(ex));
            }
        }
    }

    internal static string Describe(Exception ex) => ex switch
    {
        WalletException => ex.Message,
        TaskCanceledException => "Karşı taraf zamanında yanıt vermedi.",
        _ => "Karşı tarafa ulaşılamadı.",
    };
}
