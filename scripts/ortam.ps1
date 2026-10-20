# Kullanıcı klasörüne kurulu .NET SDK'yı bu PowerShell oturumunda etkinleştirir.
#
# Kullanım (baştaki nokta ve boşluk önemli, betik mevcut oturumu değiştirir):
#   . .\scripts\ortam.ps1
#
# Neden gerekli: C:\Program Files\dotnet altında yalnız runtime var, SDK yok.
# SDK, yönetici yetkisi istemeden %USERPROFILE%\.dotnet altına kuruldu.

$sdk = Join-Path $env:USERPROFILE ".dotnet"
if (-not (Test-Path (Join-Path $sdk "dotnet.exe"))) {
    throw "SDK bulunamadı: $sdk. Kurulum: dotnet-install.ps1 -Channel 10.0 -InstallDir $sdk"
}

$env:DOTNET_ROOT = $sdk
if (-not ($env:PATH -split ';' | Where-Object { $_ -eq $sdk })) {
    $env:PATH = "$sdk;$env:PATH"
}

# Kullanım verisi (telemetri) Microsoft'a gönderilmesin.
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

Write-Host "SDK: $(dotnet --version)  ($sdk)"
