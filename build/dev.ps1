<#
.SYNOPSIS
  SenseİK geliştirici kısayolları.
.EXAMPLE
  .\build\dev.ps1 up | down | reset | migrate | seed | gen-api | test | api | worker | web
#>
param(
  [Parameter(Position = 0)]
  [ValidateSet('up', 'down', 'reset', 'migrate', 'seed', 'gen-api', 'test', 'api', 'worker', 'web')]
  [string]$Cmd = 'up',
  [string]$Profile = 'demo'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compose = Join-Path $root 'infra/docker-compose.yml'
switch ($Cmd) {
  'up'      { docker compose -f $compose up -d }
  'down'    { docker compose -f $compose down }
  'reset'   { docker compose -f $compose down -v; docker compose -f $compose up -d }
  'migrate' { dotnet run --project (Join-Path $root 'src/Sense.Migrator') -- migrate }
  'seed'    { dotnet run --project (Join-Path $root 'src/Sense.Migrator') -- seed --profile $Profile }
  'gen-api' { pnpm --filter @senseik/api-client gen }
  'test'    { dotnet test $root }
  'api'     { dotnet watch --project (Join-Path $root 'src/Sense.Api') }
  'worker'  { dotnet run --project (Join-Path $root 'src/Sense.Worker') }
  'web'     { pnpm --filter web dev }
}
