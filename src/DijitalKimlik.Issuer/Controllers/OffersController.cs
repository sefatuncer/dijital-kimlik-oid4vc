using DijitalKimlik.Issuer.Oid4Vci;

using Microsoft.AspNetCore.Mvc;

namespace DijitalKimlik.Issuer.Controllers;

/// <summary>Memurun "Kimlik bilgisi teklifi oluştur" düğmesi.</summary>
public class OffersController(OfferService offers) : Controller
{
    /// <summary>
    /// Memur girişi henüz yok (yol haritasında). O gelene kadar teklifi oluşturan kişi sabit bir
    /// demo hesabı olarak kaydedilir.
    /// </summary>
    private const string DemoOfficer = "demo-memur";

    [HttpPost("/offers")]
    [ValidateAntiForgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Create(int permitId, CancellationToken cancellationToken)
    {
        var offer = await offers.CreateAsync(permitId, User.Identity?.Name ?? DemoOfficer, cancellationToken);
        if (offer is null)
        {
            TempData["Error"] = "İzin bulunamadı ya da bugün geçerli değil; teklif oluşturulmadı.";
            return RedirectToAction("Index", "Home");
        }

        return View("Created", offer);
    }
}
