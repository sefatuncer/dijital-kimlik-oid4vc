using DijitalKimlik.Domain.Common;
using DijitalKimlik.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DijitalKimlik.Issuer.Controllers;

/// <summary>
/// Askıya alma, yeniden etkinleştirme ve iptal. Durum yalnız veritabanında değişir; doğrulayıcı
/// bunu bir sonraki durum listesi indirmesinde görür.
/// </summary>
public class CredentialsController(IssuerDbContext db, TimeProvider clock) : Controller
{
    [HttpPost("/credentials/{id:int}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, string operation, string? reason, CancellationToken cancellationToken)
    {
        var credential = await db.IssuedCredentials.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (credential is null)
        {
            return NotFound();
        }

        var now = clock.GetUtcNow();
        try
        {
            switch (operation)
            {
                case "suspend":
                    credential.Suspend(reason ?? string.Empty, now);
                    break;
                case "reinstate":
                    credential.Reinstate(now);
                    break;
                case "revoke":
                    credential.Revoke(reason ?? string.Empty, now);
                    break;
                default:
                    return BadRequest();
            }

            await db.SaveChangesAsync(cancellationToken);
            TempData["Message"] = "Durum güncellendi: durum listesinin bir sonraki indirmesinde görünür.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (DbUpdateConcurrencyException)
        {
            // İki memur aynı kaydı aynı anda değiştirdi: rowversion ikincisini durdurdu.
            TempData["Error"] = "Kayıt bu arada başka biri tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin.";
        }

        return RedirectToAction("Index", "Home");
    }
}
