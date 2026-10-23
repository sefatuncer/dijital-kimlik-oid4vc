using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace DijitalKimlik.Wallet.Credentials;

/// <summary>
/// Her cüzdanın kimlik bilgileri ayrı bir dosyada, ASP.NET Core Data Protection ile şifreli durur
/// (AES-256-CBC + HMACSHA256). Dosyayı ele geçiren, Data Protection anahtarları olmadan içini okuyamaz.
/// </summary>
/// <remarks>
/// Cüzdan kimliği bir çerezden gelir (<see cref="WalletSession"/>). Bir kullanıcı ancak kendi
/// cüzdan kimliğinin dosyasını okuyabilir; başka bir kimlik bilgisinin numarasını bilmek yetmez (IDOR).
/// </remarks>
public sealed class WalletStore(IDataProtectionProvider dataProtection, IHostEnvironment environment, IOptions<WalletOptions> options)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("DijitalKimlik.Wallet.Store.v1");
    private readonly string _directory = Path.Combine(environment.ContentRootPath, options.Value.StorePath);

    // Tek sunuculu demo için yeterli. Birden çok sunucuda saklama bir veritabanına taşınır.
    private readonly Lock _gate = new();

    public IReadOnlyList<StoredCredential> List(string walletId)
    {
        lock (_gate)
        {
            return Load(walletId);
        }
    }

    public StoredCredential? Find(string walletId, string credentialId) =>
        List(walletId).FirstOrDefault(c => c.Id == credentialId);

    public void Add(string walletId, StoredCredential credential)
    {
        lock (_gate)
        {
            var all = Load(walletId);
            all.Add(credential);
            Save(walletId, all);
        }
    }

    public bool Remove(string walletId, string credentialId)
    {
        lock (_gate)
        {
            var all = Load(walletId);
            var removed = all.RemoveAll(c => c.Id == credentialId) > 0;
            Save(walletId, all);
            return removed;
        }
    }

    public static string NewCredentialId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(12));

    private List<StoredCredential> Load(string walletId)
    {
        var path = PathFor(walletId);
        if (!File.Exists(path))
        {
            return [];
        }

        var json = _protector.Unprotect(File.ReadAllText(path));
        return JsonSerializer.Deserialize<List<StoredCredential>>(json) ?? [];
    }

    private void Save(string walletId, List<StoredCredential> credentials)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(walletId), _protector.Protect(JsonSerializer.Serialize(credentials)));
    }

    private string PathFor(string walletId) =>
        WalletSession.IsValidId(walletId)
            ? Path.Combine(_directory, walletId + ".dat")
            : throw new ArgumentException("Geçersiz cüzdan kimliği.", nameof(walletId));
}
