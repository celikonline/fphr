using System.Security.Cryptography;
using System.Text;

namespace Sense.Modules.Payroll.Domain.Payslips;

/// <summary>Computes <see cref="PayslipDelivery.EvidenceHash"/> from <see cref="Payslip.InputHash"/> + <see
/// cref="Payslip.Version"/> - deterministic, no PII/amount involved (only the calculation's own content hash and
/// its version number).</summary>
public static class PayslipEvidenceHash
{
    public static string Compute(string inputHash, int version) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{inputHash}:{version}")));
}
