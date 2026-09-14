# Yerel PostgreSQL 'postgres' kullanıcısının şifresini sıfırlar (yalnızca geliştirici makinesi).
# Yönetici PowerShell gerektirir. pg_hba.conf geçici olarak trust yapılır, işlem sonunda geri alınır.
param(
  [string]$PgVersion = "18",
  [string]$NewPassword = "postgres",
  [string]$DataDir = "C:\pgdata"
)
$ErrorActionPreference = "Stop"
$base = "C:\Program Files\PostgreSQL\$PgVersion"
$svc = "postgresql-x64-$PgVersion"
$hba = Join-Path $DataDir "pg_hba.conf"
$bak = Join-Path $DataDir "pg_hba.conf.senseik-bak"
$log = Join-Path $env:TEMP "senseik-pg-reset.log"

"START $(Get-Date -Format s) data=$DataDir" | Out-File $log
try {
  if (-not (Test-Path $hba)) { throw "pg_hba.conf bulunamadı: $hba" }
  Copy-Item $hba $bak -Force
  $orig = Get-Content $hba
  $trust = @("host all all 127.0.0.1/32 trust", "host all all ::1/128 trust")
  Set-Content -Path $hba -Value ($trust + $orig) -Encoding ascii
  Restart-Service $svc
  Start-Sleep -Seconds 4
  $env:PGPASSWORD = ""
  & "$base\bin\psql.exe" -U postgres -h 127.0.0.1 -p 5432 -v ON_ERROR_STOP=1 -c "ALTER USER postgres PASSWORD '$NewPassword';" 2>&1 | Out-File $log -Append
  "ALTER exit=$LASTEXITCODE" | Out-File $log -Append
}
catch {
  "ERROR: $_" | Out-File $log -Append
}
finally {
  if (Test-Path $bak) { Copy-Item $bak $hba -Force; Remove-Item $bak -Force }
  Restart-Service $svc
  "RESTORED $(Get-Date -Format s)" | Out-File $log -Append
}
