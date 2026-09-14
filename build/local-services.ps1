<#
.SYNOPSIS
  Docker'sız geliştirici makinesi için yerel servisler: PostgreSQL (Windows servisi) ve Redis (Windows derlemesi).
.EXAMPLE
  .\build\local-services.ps1 start | stop | status
.NOTES
  Redis ikilisi: $env:SENSE_REDIS_HOME (varsayılan C:\Users\<kullanıcı>\algosense\tools\redis\Redis-8.10.1-Windows-x64-msys2)
  PostgreSQL servisi: $env:SENSE_PG_SERVICE (varsayılan postgresql-x64-18)
#>
param(
  [Parameter(Position = 0)] [ValidateSet('start', 'stop', 'status')] [string]$Cmd = 'status'
)
$ErrorActionPreference = 'Stop'
$redisHome = if ($env:SENSE_REDIS_HOME) { $env:SENSE_REDIS_HOME } else { Join-Path $env:USERPROFILE 'algosense\tools\redis\Redis-8.10.1-Windows-x64-msys2' }
$pgService = if ($env:SENSE_PG_SERVICE) { $env:SENSE_PG_SERVICE } else { 'postgresql-x64-18' }
$redisPort = if ($env:SENSE_REDIS_PORT) { [int]$env:SENSE_REDIS_PORT } else { 6379 }

function Get-RedisProcess { Get-Process -Name 'redis-server' -ErrorAction SilentlyContinue }

switch ($Cmd) {
  'start' {
    $svc = Get-Service -Name $pgService -ErrorAction SilentlyContinue
    if ($null -ne $svc -and $svc.Status -ne 'Running') { Start-Service $pgService }
    if (-not (Get-RedisProcess)) {
      Start-Process -FilePath (Join-Path $redisHome 'redis-server.exe') -ArgumentList "--port $redisPort --save `"`" --appendonly no" -WindowStyle Hidden
    }
    Write-Host "PostgreSQL: $((Get-Service $pgService).Status); Redis: started on $redisPort"
  }
  'stop' {
    Get-RedisProcess | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Host 'Redis stopped (PostgreSQL service left running).'
  }
  'status' {
    $svc = Get-Service -Name $pgService -ErrorAction SilentlyContinue
    $pg = if ($null -eq $svc) { 'not installed' } else { $svc.Status }
    $redis = if (Get-RedisProcess) { 'running' } else { 'stopped' }
    Write-Host "PostgreSQL ($pgService): $pg"
    Write-Host "Redis (:$redisPort): $redis"
  }
}
