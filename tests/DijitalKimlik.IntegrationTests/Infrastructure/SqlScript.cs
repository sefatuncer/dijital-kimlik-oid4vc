using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;

namespace DijitalKimlik.IntegrationTests.Infrastructure;

/// <summary>
/// Repodaki sqlcmd biçimli betikleri testte çalıştırır. sqlcmd'nin iki özelliği taklit edilir:
/// <c>$(Ad)</c> değişkenleri ve batch ayırıcı <c>GO</c>. <c>GO</c> bir T-SQL komutu değildir; onu
/// sqlcmd ve SSMS tanır, SqlCommand tanımaz.
/// </summary>
internal static partial class SqlScript
{
    public static async Task RunAsync(string connectionString, string path, IReadOnlyDictionary<string, string> variables)
    {
        var text = await File.ReadAllTextAsync(path);
        foreach (var (name, value) in variables)
        {
            text = text.Replace($"$({name})", value, StringComparison.Ordinal);
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var batch in GoSeparator().Split(text).Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Test çıktı klasöründen yukarı çıkarak repo kökünü (DijitalKimlik.slnx) bulur.</summary>
    public static string RepositoryFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DijitalKimlik.slnx")))
            {
                return Path.Combine([dir.FullName, .. parts]);
            }
        }

        throw new DirectoryNotFoundException("Repo kökü (DijitalKimlik.slnx) bulunamadı.");
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
