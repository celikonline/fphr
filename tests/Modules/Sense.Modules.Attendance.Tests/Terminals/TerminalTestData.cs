using Microsoft.Extensions.Options;
using NSubstitute;
using Sense.Modules.Attendance.Application.Terminals;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain.Terminals;
using Sense.Modules.Attendance.Infrastructure.Terminals;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Tests.Terminals;

/// <summary>Terminal testleri için ortak veri: aynı önekli anahtarlar, terminal üretimi, pepper'lı hasher, idempotent sahte recorder.</summary>
internal static class TerminalTestData
{
    /// <summary>Yalnız testlerde; gerçek ortam değeri gizli yapılandırmadan gelir.</summary>
    public const string TestPepper = "test-only-terminal-credential-pepper-0123456789";

    public static string KeyWithPrefix(string prefix)
    {
        if (prefix.Length != TerminalApiKey.PrefixLength)
        {
            throw new ArgumentException("prefix length", nameof(prefix));
        }

        return prefix + TerminalApiKey.Generate().Key[TerminalApiKey.PrefixLength..];
    }

    public static Terminal NewTerminal(Guid tenantId, string key, TerminalDirectionMode mode = TerminalDirectionMode.Both, string code = "T-01") =>
        Terminal.Create(tenantId, code, "Ana giriş", null, null, mode, TerminalApiKey.Hash(key), TerminalApiKey.PrefixOf(key)).Value;

    public static HmacTerminalCredentialHasher Hasher(string? pepper = TestPepper) =>
        new(Options.Create(new TerminalSecurityOptions { CredentialPepper = pepper }));

    /// <summary>Pepper'lı hasher için özet (başarısızsa test patlar).</summary>
    public static string Digest(this ITerminalCredentialHasher hasher, Guid tenantId, TerminalCredentialKind kind, string raw) =>
        hasher.Hash(tenantId, kind, raw).Value;

    /// <summary>İstenen çalışanları "Active" (ya da <paramref name="statuses"/>'taki durumla) döndüren kişi araması.</summary>
    public static IEmployeeContactLookup Contacts(IReadOnlyDictionary<Guid, string>? statuses = null)
    {
        var lookup = Substitute.For<IEmployeeContactLookup>();
        lookup.GetContactsAsync(default, default!, default).ReturnsForAnyArgs(call => call.ArgAt<IReadOnlyCollection<Guid>>(1)
            .Select(id => new EmployeeContact(id, "Ad", "Soyad", null, null, null, statuses?.GetValueOrDefault(id) ?? "Active"))
            .ToList());
        return lookup;
    }
}

/// <summary>
/// IPunchRecorder sözleşmesinin (B1) davranışını taklit eden bellek içi recorder: tekillik anahtarı (Tenant, Employee, OccurredAtUtc, Direction,
/// Source) - aynısı ikinci kez gelirse "ignored"; <see cref="LockedEmployees"/> kilitli ay ("rejected"); <see cref="Failures"/> hata.
/// </summary>
internal sealed class FakePunchRecorder : IPunchRecorder
{
    private readonly HashSet<(Guid, Guid, DateTime, PunchDirectionDto, PunchSourceDto)> _stored = [];

    public List<PunchInput> Received { get; } = [];

    public HashSet<Guid> LockedEmployees { get; } = [];

    public Dictionary<Guid, Error> Failures { get; } = [];

    public Task<Result<Guid>> RecordAsync(PunchInput input, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<PunchBatchResult>> RecordBatchAsync(IReadOnlyList<PunchInput> inputs, CancellationToken ct)
    {
        int accepted = 0, ignored = 0, rejected = 0;
        foreach (var input in inputs)
        {
            Received.Add(input);
            if (Failures.TryGetValue(input.EmployeeId, out var error))
            {
                return Task.FromResult<Result<PunchBatchResult>>(error);
            }

            if (LockedEmployees.Contains(input.EmployeeId))
            {
                rejected++;
            }
            else if (_stored.Add((input.TenantId, input.EmployeeId, input.OccurredAtUtc, input.Direction, input.Source)))
            {
                accepted++;
            }
            else
            {
                ignored++;
            }
        }

        return Task.FromResult<Result<PunchBatchResult>>(new PunchBatchResult(accepted, ignored, rejected));
    }

    public Task<Result> SoftDeleteAsync(Guid tenantId, Guid punchId, Guid userId, string reason, CancellationToken ct) => throw new NotSupportedException();
}
