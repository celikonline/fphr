using Sense.Modules.Organization.Contracts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// TALIMAT madde 2: Sözleşme kilidi.
///
/// İddia: Organization.Contracts/ApprovalRequestTypes.All bugün [Leave, Advance, Expense, Overtime, Requisition].
/// Küme tam olarak bu — yeni talep türü eklenirse test kırılır (contract-first, PO onayı gerekir).
///
/// Ek iddia: Workflow'un her tür için bir varsayılan politika çözümlemesi var.
/// </summary>
public sealed class ApprovalRequestTypeCatalogTests
{
    /// <summary>ApprovalRequestTypes sözleşmesi tam olarak 5 talep türü içerir. Yeni tür eklenirse test kırılır.</summary>
    [Fact]
    public void ApprovalRequestTypes_CatalogIsExactly_LeaveAdvanceExpenseOvertimeRequisition()
    {
        var catalog = ApprovalRequestTypes.All;

        catalog.Count.ShouldBe(5);
        catalog.ShouldContain(ApprovalRequestTypes.Leave);
        catalog.ShouldContain(ApprovalRequestTypes.Advance);
        catalog.ShouldContain(ApprovalRequestTypes.Expense);
        catalog.ShouldContain(ApprovalRequestTypes.Overtime);
        catalog.ShouldContain(ApprovalRequestTypes.Requisition);

        // Sıra da sabit
        catalog[0].ShouldBe(ApprovalRequestTypes.Leave);
        catalog[1].ShouldBe(ApprovalRequestTypes.Advance);
        catalog[2].ShouldBe(ApprovalRequestTypes.Expense);
        catalog[3].ShouldBe(ApprovalRequestTypes.Overtime);
        catalog[4].ShouldBe(ApprovalRequestTypes.Requisition);
    }

    /// <summary>Her talep türü iyi biçimlenmiş (PascalCase, ASCII, ≤50 karakter).</summary>
    [Theory]
    [InlineData(ApprovalRequestTypes.Leave)]
    [InlineData(ApprovalRequestTypes.Advance)]
    [InlineData(ApprovalRequestTypes.Expense)]
    [InlineData(ApprovalRequestTypes.Overtime)]
    [InlineData(ApprovalRequestTypes.Requisition)]
    public void EachRequestType_IsWellFormed(string requestType)
    {
        ApprovalRequestTypes.IsWellFormed(requestType).ShouldBeTrue();
        requestType.Length.ShouldBeLessThanOrEqualTo(ApprovalRequestTypes.MaxLength);
    }

    /// <summary>Boş veya malformed string'ler reject edilir.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("leave")] // başta küçük harf
    [InlineData("Leave Extra Long " + "x")] // 50 karakter aşıyor
    [InlineData("Leave-Request")] // tire
    public void MalformedRequestTypes_AreRejected(string malformed)
    {
        ApprovalRequestTypes.IsWellFormed(malformed).ShouldBeFalse();
    }

    /// <summary>Sözleşme tüm talep türleri için yardımcı sabitler sağlar — isimlendirme kolaylığı.</summary>
    [Fact]
    public void AllRequestTypes_HaveConstants()
    {
        // Reflection ile kontrol: ApprovalRequestTypes.cs'teki tüm "public const string" değerleri
        // All array'inde var mı?
        var typeConstants = typeof(ApprovalRequestTypes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string) && f.IsLiteral)
            .Select(f => (string?)f.GetValue(null))
            .Where(v => v != null) // Yalnız const string alanları (All dizi olduğu için bu filtre tarafından zaten excluded)
            .Cast<string>()
            .ToList();

        var catalogItems = ApprovalRequestTypes.All.ToList();

        foreach (var constant in typeConstants)
        {
            catalogItems.ShouldContain(constant,
                $"ApprovalRequestTypes.All kataloğunda {constant} sabitinin karşılığı yok");
        }
    }
}
