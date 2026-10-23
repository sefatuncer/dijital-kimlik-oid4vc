using System.Diagnostics;

using DijitalKimlik.Wallet.Credentials;
using DijitalKimlik.Wallet.Models;

using Microsoft.AspNetCore.Mvc;

namespace DijitalKimlik.Wallet.Controllers;

/// <summary>Cüzdanın ana ekranı: kimlik bilgilerim.</summary>
public class HomeController(WalletStore store) : Controller
{
    public IActionResult Index()
    {
        var walletId = WalletSession.GetOrCreateWalletId(HttpContext);
        return View(store.List(walletId));
    }

    [HttpPost("/credentials/{id}/delete")]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(string id)
    {
        var walletId = WalletSession.GetOrCreateWalletId(HttpContext);
        TempData["Message"] = store.Remove(walletId, id) ? "Kimlik bilgisi cüzdandan silindi." : "Kimlik bilgisi bulunamadı.";
        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
