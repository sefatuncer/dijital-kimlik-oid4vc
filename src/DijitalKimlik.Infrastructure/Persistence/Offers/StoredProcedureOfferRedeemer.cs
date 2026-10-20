using System.Data;

using DijitalKimlik.Application.Offers;
using DijitalKimlik.Domain.Offers;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DijitalKimlik.Infrastructure.Persistence.Offers;

/// <summary>
/// Saklı yordam (<c>issuer.usp_RedeemOffer</c>) ile tek turda kullanım. Çağrı ADO.NET ile yapılır:
/// <c>CommandType.StoredProcedure</c>, tipi ve boyutu açıkça verilmiş parametreler, DataReader.
/// </summary>
/// <remarks>
/// <para>Atomik yöntem gibi kararı veritabanı verir, ama <c>UPDATE ... OUTPUT</c> sayesinde
/// güncellenen satırın yeni durumunu aynı komutta alır: yanlış PIN'de WrongTxCode ile Locked kesin
/// ayrılır, ek okuma yoktur.</para>
/// <para>Bedeli: kural T-SQL'de yaşar (Domain'deki kuralla birlikte iki yerde), sürümlemesi
/// migration ile yapılır ve birim testi C# kadar kolay değildir.</para>
/// <para>Alternatif çağrı: <c>db.Database.SqlQuery&lt;T&gt;($"EXEC ...")</c>. EF bu durumda
/// sorguyu oluşturamaz (composable değil); sonucu doğrudan <c>ToListAsync</c> ile almak gerekir,
/// <c>FirstOrDefaultAsync</c> SQL'i alt sorguya sarmaya çalışır ve hata verir.</para>
/// </remarks>
internal sealed class StoredProcedureOfferRedeemer(IssuerDbContext db, TimeProvider time) : IOfferRedeemer
{
    public OfferRedemptionStrategy Strategy => OfferRedemptionStrategy.StoredProcedure;

    public async Task<OfferRedemption?> RedeemAsync(string preAuthorizedCode, string txCode, CancellationToken cancellationToken = default)
    {
        // Bağlantıyı EF yönetir: daha önce açılmışsa açık bırakır, burada açtıysa kapatır.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "issuer.usp_RedeemOffer";
            command.CommandType = CommandType.StoredProcedure;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            // AddWithValue yerine tip ve boyut açık: aksi halde nvarchar(uzunluk) gönderilir, her
            // farklı uzunluk ayrı bir sorgu planı üretir ve char(43) sütununda örtük dönüşüm olur.
            command.Parameters.Add(new SqlParameter("@PreAuthorizedCodeHash", SqlDbType.Char, 43)
            {
                Value = CredentialOffer.HashPreAuthorizedCode(preAuthorizedCode),
            });
            command.Parameters.Add(new SqlParameter("@TxCodeHash", SqlDbType.Char, 43)
            {
                Value = CredentialOffer.HashTxCode(preAuthorizedCode, txCode),
            });
            command.Parameters.Add(new SqlParameter("@Now", SqlDbType.DateTimeOffset) { Value = time.GetUtcNow() });
            command.Parameters.Add(new SqlParameter("@MaxAttempts", SqlDbType.Int) { Value = CredentialOffer.MaxTxCodeAttempts });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new OfferRedemption(
                (RedeemResult)reader.GetInt32(reader.GetOrdinal("Result")),
                reader.GetInt32(reader.GetOrdinal("OfferId")),
                reader.GetInt32(reader.GetOrdinal("ResidencePermitId")));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
