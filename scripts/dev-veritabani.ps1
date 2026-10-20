# Yerel geliştirme veritabanını sıfırdan hazırlar. Tekrar çalıştırmak güvenlidir (idempotent).
#
#   . .\scripts\ortam.ps1
#   .\scripts\dev-veritabani.ps1
#
# Adımlar:
#   1. deploy/dev.local.env yoksa rastgele şifrelerle üretir (git'e girmez)
#   2. SQL Server konteynerini başlatır ve hazır olmasını bekler
#   3. EF Core migration'larını yönetici (sa) hesabıyla uygular
#   4. Uygulama kullanıcısını (dk_issuer_app) en az yetkiyle oluşturur
#   5. Uygulamanın bağlantı dizesini user-secrets'a yazar (appsettings.json'a değil)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$envFile = Join-Path $root "deploy\dev.local.env"
$compose = Join-Path $root "deploy\docker-compose.dev.yml"

# Dış komutlar (docker, dotnet, sqlcmd) ilerleme mesajlarını stderr'e yazar. PowerShell 5.1'de
# ErrorActionPreference=Stop iken stderr satırı hata sayılır ve betik durur. Bu yüzden dış komutun
# başarısı stderr'e değil çıkış koduna bakılarak değerlendirilir.
function Invoke-Native([string]$What, [scriptblock]$Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & $Command } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "$What başarısız (çıkış kodu $LASTEXITCODE)" }
}

function New-DevPassword {
    # Windows PowerShell 5.1 .NET Framework üzerinde çalışır; RandomNumberGenerator.GetInt32 orada yok.
    $chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789".ToCharArray()
    $bytes = New-Object byte[] 24
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    # Sonuna her karakter sınıfından birer tane: SQL Server parola ilkesi garanti sağlanır.
    return (-join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })) + "-Aa1"
}

if (-not (Test-Path $envFile)) {
    "MSSQL_SA_PASSWORD=$(New-DevPassword)`nDK_ISSUER_APP_PASSWORD=$(New-DevPassword)`n" |
        Set-Content -Path $envFile -Encoding ascii -NoNewline
    Write-Host "Şifreler üretildi: $envFile"
}

$vars = @{}
foreach ($line in Get-Content $envFile) {
    $i = $line.IndexOf("=")
    if ($i -gt 0 -and -not $line.TrimStart().StartsWith("#")) {
        $vars[$line.Substring(0, $i).Trim()] = $line.Substring($i + 1).Trim()
    }
}

Write-Host "SQL Server başlatılıyor..."
Invoke-Native "docker compose" { docker compose -f $compose --env-file $envFile up -d --wait }

# "localhost" değil: Windows'ta localhost önce IPv6 (::1) çözülür, Docker ise portu yalnız
# 127.0.0.1'de açar. SqlClient ::1'i dener ve zaman aşımına uğrar (SQL hata 258).
$server = "127.0.0.1,1433"
$adminCs = "Server=$server;Database=DijitalKimlik;User Id=sa;Password=$($vars.MSSQL_SA_PASSWORD);TrustServerCertificate=True"
$appCs = "Server=$server;Database=DijitalKimlik;User Id=dk_issuer_app;Password=$($vars.DK_ISSUER_APP_PASSWORD);TrustServerCertificate=True;Application Name=DijitalKimlik.Issuer"

Write-Host "Migration'lar uygulanıyor..."
$inf = Join-Path $root "src\DijitalKimlik.Infrastructure"
Invoke-Native "dotnet ef database update" { dotnet ef database update -p $inf -s $inf --context IssuerDbContext --connection $adminCs }

Write-Host "Uygulama kullanıcısı oluşturuluyor..."
# Betik dosyası konteynere kopyalanıp -i ile çalıştırılır. Pipe ile verilseydi PowerShell'in
# $OutputEncoding ayarı başa BOM ekleyebiliyor; sqlcmd onu SQL sanıp "Incorrect syntax" veriyor.
Invoke-Native "docker cp" { docker cp (Join-Path $root "db\00-uygulama-kullanicilari.sql") dk-mssql:/tmp/00-uygulama-kullanicilari.sql }
Invoke-Native "sqlcmd" {
    docker exec dk-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $vars.MSSQL_SA_PASSWORD -b -f 65001 `
        -i /tmp/00-uygulama-kullanicilari.sql -v "IssuerAppPassword=$($vars.DK_ISSUER_APP_PASSWORD)" "DatabaseName=DijitalKimlik"
}

Write-Host "Bağlantı dizesi user-secrets'a yazılıyor..."
Invoke-Native "dotnet user-secrets" {
    dotnet user-secrets set "ConnectionStrings:IssuerDb" $appCs --project (Join-Path $root "src\DijitalKimlik.Issuer") | Out-Null
}

Write-Host "Hazır. Issuer: dotnet run --project src\DijitalKimlik.Issuer  ->  http://localhost:5101"
