using QRCoder;

namespace DijitalKimlik.Issuer.Models;

/// <summary>Teklif bağlantısını QR kodlu PNG'ye çevirir; sayfaya data URI olarak gömülür.</summary>
public static class QrCode
{
    public static string ToDataUri(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
        using var png = new PngByteQRCode(data);
        return "data:image/png;base64," + Convert.ToBase64String(png.GetGraphic(4));
    }
}
