using DijitalKimlik.Verifier.Models;
using DijitalKimlik.Verifier.Presentations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DijitalKimlik.Verifier.Controllers;

/// <summary>Doğrulama oturumu: isteği göster (QR ve bağlantı), sonucu bekle, sonucu göster.</summary>
public class PresentationsController(TransactionStore transactions, IOptions<VerifierOptions> options) : Controller
{
    private const string SessionCookie = "dk-verifier";

    [HttpPost("/presentations")]
    [ValidateAntiForgeryToken]
    public IActionResult Start(string scenario)
    {
        var selected = Scenario.Find(scenario);
        if (selected is null)
        {
            return BadRequest();
        }

        var transaction = transactions.Create(selected, SessionId());
        return RedirectToAction(nameof(Show), new { id = transaction.Id });
    }

    [HttpGet("/presentations/{id}")]
    public IActionResult Show(string id)
    {
        var transaction = transactions.Find(id);
        if (transaction is null || !transaction.OwnedBy(Request.Cookies[SessionCookie]))
        {
            return NotFound();
        }

        var query = transactions.AuthorizationQuery(transaction);
        var walletLink = options.Value.WalletUrl.TrimEnd('/') + "/authorize?" + query;
        var deepLink = "openid4vp://?" + query;
        return View(new RequestViewModel(transaction, walletLink, deepLink, QrCode.ToDataUri(deepLink)));
    }

    /// <summary>İstek sayfası bunu iki saniyede bir sorar (polling). Alternatif: SignalR ile anlık bildirim.</summary>
    [HttpGet("/presentations/{id}/status")]
    public IActionResult Status(string id)
    {
        var transaction = transactions.Find(id);
        if (transaction is null || !transaction.OwnedBy(Request.Cookies[SessionCookie]))
        {
            return NotFound();
        }

        return Json(new { done = transaction.Result is not null, resultUrl = Url.Action(nameof(Result), new { id }) });
    }

    /// <summary>
    /// Sonuç ya oturumu açan tarayıcıya (çerez) ya da cüzdanın yönlendirdiği tarayıcıya
    /// (<c>response_code</c>) gösterilir. Bağlantıyı bilen üçüncü biri sonucu göremez.
    /// </summary>
    [HttpGet("/presentations/{id}/result")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Result(string id, [FromQuery(Name = "response_code")] string? responseCode)
    {
        var transaction = transactions.Find(id);
        if (transaction is null || !(transaction.OwnedBy(Request.Cookies[SessionCookie]) || transaction.AcceptsCode(responseCode)))
        {
            return NotFound();
        }

        if (transaction.Result is null)
        {
            return RedirectToAction(nameof(Show), new { id });
        }

        return View(transaction);
    }

    private string SessionId()
    {
        if (Request.Cookies[SessionCookie] is { Length: 22 } existing)
        {
            return existing;
        }

        var id = TransactionStore.Random(16);
        Response.Cookies.Append(SessionCookie, id, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
        });
        return id;
    }
}
