using System.Diagnostics;

using DijitalKimlik.Infrastructure.Persistence;
using DijitalKimlik.Issuer.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DijitalKimlik.Issuer.Controllers;

/// <summary>Memur paneli: ikamet izinleri ve verilmiş kimlik bilgileri.</summary>
public class HomeController(IssuerDbContext db, TimeProvider clock) : Controller
{
    private const int RecentCredentialCount = 50;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // Okuma ekranı: AsNoTracking ile değişiklik izleme maliyeti yok.
        var permits = await db.ResidencePermits
            .AsNoTracking()
            .Include(p => p.Foreigner)
            .OrderBy(p => p.PermitNumber)
            .ToListAsync(cancellationToken);

        var credentials = await db.IssuedCredentials
            .AsNoTracking()
            .Include(c => c.ResidencePermit).ThenInclude(p => p.Foreigner)
            .OrderByDescending(c => c.IssuedAt)
            .Take(RecentCredentialCount)
            .ToListAsync(cancellationToken);

        var model = new DashboardViewModel(
            [.. permits.Select(p => new PermitRow(
                p.Id,
                p.Foreigner.GivenName + " " + p.Foreigner.FamilyName,
                Labels.Mask(p.Foreigner.IdentityNumber.Value),
                p.Foreigner.Nationality,
                p.PermitNumber,
                p.Type,
                p.ValidFrom,
                p.ValidUntil,
                p.IsValidOn(today)))],
            [.. credentials.Select(c => new CredentialRow(
                c.Id,
                c.CredentialId,
                c.ResidencePermit.Foreigner.GivenName + " " + c.ResidencePermit.Foreigner.FamilyName,
                c.ResidencePermit.PermitNumber,
                c.Status,
                c.StatusReason,
                c.StatusListIndex,
                c.IssuedAt,
                c.ExpiresAt))]);
        return View(model);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
