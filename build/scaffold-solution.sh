#!/usr/bin/env bash
# Faz 0 çözüm iskeleti üretici. Tek seferlik; mevcut csproj dosyalarını ezer.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="/c/Program Files/dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

dotnet new sln -n Sense --force >/dev/null && echo "sln ok"

ASPNET_REF='  <ItemGroup>\n    <FrameworkReference Include="Microsoft.AspNetCore.App" />\n  </ItemGroup>\n'

# mkproj NAME DIR SDK TYPE(lib|exe|web|test) REFS PKGS [aspnet]
mkproj() {
  local name="$1" dir="$2" sdk="$3" type="$4" refs="$5" pkgs="$6" aspnet="${7:-}"
  mkdir -p "$dir"
  {
    printf '<Project Sdk="%s">\n\n  <PropertyGroup>\n' "$sdk"
    if [ "$type" = "exe" ] || [ "$type" = "web" ]; then printf '    <OutputType>Exe</OutputType>\n'; fi
    if [ "$type" = "web" ]; then printf '    <UserSecretsId>senseik-%s</UserSecretsId>\n' "$name"; fi
    if [ "$type" = "test" ]; then printf '    <IsPackable>false</IsPackable>\n    <IsTestProject>true</IsTestProject>\n'; fi
    printf '  </PropertyGroup>\n'
    if [ "$aspnet" = "aspnet" ]; then printf '\n'; printf "$ASPNET_REF"; fi
    if [ -n "$refs" ]; then printf '\n  <ItemGroup>\n'; for r in $refs; do printf '    <ProjectReference Include="%s" />\n' "$r"; done; printf '  </ItemGroup>\n'; fi
    if [ -n "$pkgs" ]; then printf '\n  <ItemGroup>\n'; for p in $pkgs; do printf '    <PackageReference Include="%s" />\n' "$p"; done; printf '  </ItemGroup>\n'; fi
    printf '\n</Project>\n'
  } > "$dir/$name.csproj"
  dotnet sln Sense.slnx add "$dir/$name.csproj" >/dev/null && echo "added $name"
}

S=src/Shared
K="../Sense.Shared.Kernel/Sense.Shared.Kernel.csproj"
C="../Sense.Shared.Contracts/Sense.Shared.Contracts.csproj"
I="../Sense.Shared.Infrastructure/Sense.Shared.Infrastructure.csproj"

mkproj Sense.Shared.Kernel $S/Sense.Shared.Kernel Microsoft.NET.Sdk lib "" ""
mkproj Sense.Shared.Contracts $S/Sense.Shared.Contracts Microsoft.NET.Sdk lib "$K" \
  "Microsoft.Extensions.DependencyInjection.Abstractions Microsoft.Extensions.Configuration.Abstractions Microsoft.Extensions.Logging.Abstractions"
mkproj Sense.Shared.Infrastructure $S/Sense.Shared.Infrastructure Microsoft.NET.Sdk lib "$K $C" \
  "Microsoft.EntityFrameworkCore Microsoft.EntityFrameworkCore.Relational Npgsql.EntityFrameworkCore.PostgreSQL EFCore.NamingConventions FluentValidation FluentValidation.DependencyInjectionExtensions Scrutor Microsoft.Extensions.Caching.Hybrid Microsoft.Extensions.Caching.StackExchangeRedis StackExchange.Redis Hangfire.Core Hangfire.PostgreSql Microsoft.Extensions.Hosting Microsoft.Extensions.Options.ConfigurationExtensions Microsoft.Extensions.Http.Resilience Serilog.AspNetCore Serilog.Sinks.Console Serilog.Sinks.Seq Serilog.Enrichers.Environment Serilog.Formatting.Compact OpenTelemetry.Extensions.Hosting OpenTelemetry.Exporter.OpenTelemetryProtocol OpenTelemetry.Instrumentation.AspNetCore OpenTelemetry.Instrumentation.Http OpenTelemetry.Instrumentation.Runtime Npgsql.OpenTelemetry AWSSDK.S3 MailKit Scriban QuestPDF ClosedXML CsvHelper" aspnet
mkproj Sense.Shared.Web $S/Sense.Shared.Web Microsoft.NET.Sdk lib "$K $C $I" \
  "Microsoft.AspNetCore.Authentication.JwtBearer Asp.Versioning.Http Microsoft.AspNetCore.OpenApi Scalar.AspNetCore Hangfire.AspNetCore Microsoft.AspNetCore.SignalR.StackExchangeRedis Microsoft.FeatureManagement.AspNetCore" aspnet

# mkmod MODULE [extra-infra-packages]
mkmod() {
  local M="$1" base="src/Modules/$1" extra="${2:-}"
  local SK="../../../Shared/Sense.Shared.Kernel/Sense.Shared.Kernel.csproj"
  local SC="../../../Shared/Sense.Shared.Contracts/Sense.Shared.Contracts.csproj"
  local SI="../../../Shared/Sense.Shared.Infrastructure/Sense.Shared.Infrastructure.csproj"
  local SW="../../../Shared/Sense.Shared.Web/Sense.Shared.Web.csproj"
  mkproj Sense.Modules.$M.Domain $base/Sense.Modules.$M.Domain Microsoft.NET.Sdk lib "$SK" ""
  mkproj Sense.Modules.$M.Contracts $base/Sense.Modules.$M.Contracts Microsoft.NET.Sdk lib "$SK $SC" ""
  mkproj Sense.Modules.$M.Application $base/Sense.Modules.$M.Application Microsoft.NET.Sdk lib \
    "../Sense.Modules.$M.Domain/Sense.Modules.$M.Domain.csproj ../Sense.Modules.$M.Contracts/Sense.Modules.$M.Contracts.csproj $SC" "FluentValidation"
  mkproj Sense.Modules.$M.Infrastructure $base/Sense.Modules.$M.Infrastructure Microsoft.NET.Sdk lib \
    "../Sense.Modules.$M.Application/Sense.Modules.$M.Application.csproj $SI" "Microsoft.EntityFrameworkCore Npgsql.EntityFrameworkCore.PostgreSQL $extra" aspnet
  mkproj Sense.Modules.$M.Api $base/Sense.Modules.$M.Api Microsoft.NET.Sdk lib \
    "../Sense.Modules.$M.Application/Sense.Modules.$M.Application.csproj ../Sense.Modules.$M.Infrastructure/Sense.Modules.$M.Infrastructure.csproj $SW" "" aspnet
}

mkmod Identity "Microsoft.AspNetCore.Identity.EntityFrameworkCore System.IdentityModel.Tokens.Jwt Otp.NET"
mkmod Organization
mkmod Employee

MODAPI="../Modules/Identity/Sense.Modules.Identity.Api/Sense.Modules.Identity.Api.csproj ../Modules/Organization/Sense.Modules.Organization.Api/Sense.Modules.Organization.Api.csproj ../Modules/Employee/Sense.Modules.Employee.Api/Sense.Modules.Employee.Api.csproj"
MODINF="../Modules/Identity/Sense.Modules.Identity.Infrastructure/Sense.Modules.Identity.Infrastructure.csproj ../Modules/Organization/Sense.Modules.Organization.Infrastructure/Sense.Modules.Organization.Infrastructure.csproj ../Modules/Employee/Sense.Modules.Employee.Infrastructure/Sense.Modules.Employee.Infrastructure.csproj"

mkproj Sense.Api src/Sense.Api Microsoft.NET.Sdk.Web web "../Shared/Sense.Shared.Web/Sense.Shared.Web.csproj $MODAPI" ""
mkproj Sense.Worker src/Sense.Worker Microsoft.NET.Sdk.Worker exe "../Shared/Sense.Shared.Infrastructure/Sense.Shared.Infrastructure.csproj $MODINF" "Hangfire.AspNetCore"
mkproj Sense.Migrator src/Sense.Migrator Microsoft.NET.Sdk exe "../Shared/Sense.Shared.Infrastructure/Sense.Shared.Infrastructure.csproj $MODINF" "Microsoft.EntityFrameworkCore.Design Microsoft.Extensions.Hosting Bogus"

TESTPK="Microsoft.NET.Test.Sdk xunit.v3 xunit.runner.visualstudio Shouldly NSubstitute coverlet.collector"
mkproj Sense.Tests.Shared tests/Sense.Tests.Shared Microsoft.NET.Sdk lib "../../src/Sense.Api/Sense.Api.csproj ../../src/Sense.Migrator/Sense.Migrator.csproj" \
  "Microsoft.AspNetCore.Mvc.Testing Testcontainers.PostgreSql Testcontainers.Redis Respawn xunit.v3 Shouldly Bogus Microsoft.Extensions.TimeProvider.Testing"
mkproj Sense.Tests.Architecture tests/Sense.Tests.Architecture Microsoft.NET.Sdk test "../../src/Sense.Api/Sense.Api.csproj" "$TESTPK NetArchTest.Rules"
mkproj Sense.Tests.Integration tests/Sense.Tests.Integration Microsoft.NET.Sdk test "../Sense.Tests.Shared/Sense.Tests.Shared.csproj" "$TESTPK"
mkproj Sense.Tests.TenantIsolation tests/Sense.Tests.TenantIsolation Microsoft.NET.Sdk test "../Sense.Tests.Shared/Sense.Tests.Shared.csproj" "$TESTPK"
mkproj Sense.Tests.Contract tests/Sense.Tests.Contract Microsoft.NET.Sdk test "../Sense.Tests.Shared/Sense.Tests.Shared.csproj" "$TESTPK Verify.Xunit"
mkproj Sense.Shared.Kernel.Tests tests/Modules/Sense.Shared.Kernel.Tests Microsoft.NET.Sdk test "../../../src/Shared/Sense.Shared.Kernel/Sense.Shared.Kernel.csproj" "$TESTPK"
mkproj Sense.Modules.Identity.Tests tests/Modules/Sense.Modules.Identity.Tests Microsoft.NET.Sdk test "../../../src/Modules/Identity/Sense.Modules.Identity.Application/Sense.Modules.Identity.Application.csproj" "$TESTPK"

echo "=== projects: $(dotnet sln Sense.slnx list | tail -n +3 | wc -l)"
