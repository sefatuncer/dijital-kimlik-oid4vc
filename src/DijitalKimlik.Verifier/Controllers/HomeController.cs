using System.Diagnostics;

using DijitalKimlik.Verifier.Models;
using DijitalKimlik.Verifier.Presentations;

using Microsoft.AspNetCore.Mvc;

namespace DijitalKimlik.Verifier.Controllers;

/// <summary>Senaryo seçimi: hangi iş için hangi alanlar isteniyor?</summary>
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View(Scenario.All);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
