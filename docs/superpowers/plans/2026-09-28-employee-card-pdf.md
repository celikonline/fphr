# Çalışan Kartı PDF Çıktısı — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Çalışan kartından, seçilen sekmeleri ve dönemi içeren, sunucuda QuestPDF ile üretilmiş tek bir PDF indirmek.

**Architecture:**
- Employee.Contracts'ta `IEmployeeCardPdfSection` sözleşmesi tanımlanır.
- Her modül kendi sekmesinin sağlayıcısını kendi Infrastructure katmanında yazar ve kendi Api modülünde kaydeder.
- Sağlayıcı, modülün mevcut sorgularını `IDispatcher` ile çağırır. Böylece yetki, kapsam ve maskeleme pipeline'ı aynen çalışır.
- `EmployeeCardPdfComposer` (Employee.Application) sağlayıcıları toplar, modül açık/kapalı kontrolünü yapar ve `ISimplePdfWriter` ile PDF'i üretir.
- Web'de kart başlığına bir PDF butonu ve sekme/dönem seçim diyaloğu eklenir.

**Tech Stack:** .NET 10, QuestPDF (`ISimplePdfWriter`), xunit.v3 + Shouldly + NSubstitute; React 19 + Mantine 9 + TanStack Query + react-i18next + Vitest.

**Spec:** `docs/superpowers/specs/2026-09-28-employee-card-pdf-design.md`

## Global Constraints

- Modüller birbirine yalnızca `*.Contracts` üzerinden bağlanır (`tests/Sense.Tests.Architecture/OnionArchitectureTests.cs`).
- Sekme anahtarları web ile aynıdır: `general, personal, customFields, career, payments, payroll, documents, assets, leave, overtime, account`. `other` PDF'te yoktur.
- Tablo başına en fazla 500 satır basılır, en yeni kayıt önce gelir. Fazlası için "N kayıt daha var; dönemi daraltın." paragrafı eklenir.
- Dönem yalnızca olay türü listelere uygulanır. Kesişim kuralı: başlangıç ≤ To ve bitiş ≥ From. `from` ve `to` ikisi de boşsa tüm zamanlar alınır.
- Sorgu `Forbidden` ya da `NotFound` dönerse o parça gizlenir. Diğer hatalarda `InvalidOperationException` fırlatılır ve istek bütünüyle başarısız olur.
- PDF metinleri Türkçedir. Enum etiketleri `IStringLocalizer<SharedResource>.EnumText(...)` ile çözülür (`report.enum.{Enum}.{Member}` anahtarları).
- Dosya adı `calisan-karti-<slug>-<yyyy-MM-dd>.pdf` biçimindedir.
- Çalışma dizini paylaşımlıdır:
  - `git add` yalnızca açık dosya yollarıyla yapılır; başka oturumun değişikliği commit'lenmez.
  - Branch `dev`'dir, `main`'e yazılmaz.
- .NET: `export PATH="/c/Program Files/dotnet:$PATH"` ayarlanır. Yalnızca dokunulan proje derlenir ya da test edilir, `Sense.slnx` derlenmez.
- Web: `web/` altında `pnpm exec ...`.

## Review Focus

1. **Kapsam dışı hedef.** Zimmet ve bordro sorguları `IScopedRequest` değildir. Endpoint sorgusu `IScopedRequest` + `employees.read` olarak bu açığı kapatır. Test: Task 3, `GetEmployeeCardPdfQuery_IsScopedToTargetEmployee`.
2. **Tüm bölümler düşerse** boş PDF değil 403 dönülmeli. Test: Task 3, `Compose_WhenEverySectionIsUnavailable_IsForbidden`.
3. **Modül kapalıyken sağlayıcı hiç çağrılmamalı**, böylece veri sızmaz. Test: Task 3, `Compose_WhenModuleDisabled_SkipsProviderWithoutCallingIt`.
4. **Türkçe karakterli ad ile dosya adı.** "Şule Çağlar" → `calisan-karti-sule-caglar-2026-09-28.pdf`. Test: Task 3, `Compose_FileName_IsTurkishSlugWithDate`.
5. **Açık uçlu kayıt ve dönem.** İadesi olmayan, dönemden önce teslim edilmiş zimmet listelenmeli; bitişsiz kaydın başlangıcı dönem içindeyse kayıt listelenmeli. Testler: Task 7 `Assets_FiltersByPeriod_KeepsOpenAndPendingAssignments`, Task 2 `InPeriod_OpenEndedRecordStartingInsidePeriod_IsIncluded`.

---

## Dosya haritası

| Dosya | Sorumluluk |
|---|---|
| `src/Shared/Sense.Shared.Contracts/Export/ISimplePdfWriter.cs` (değişir) | `SimplePdfSectionHeading` blok tipi |
| `src/Shared/Sense.Shared.Infrastructure/Export/QuestPdfSimpleWriter.cs` (değişir) | Bant render'ı |
| `src/Modules/Employee/Sense.Modules.Employee.Contracts/CardPdf/IEmployeeCardPdfSection.cs` (yeni) | Sözleşme, anahtarlar, istek ve sonuç tipleri |
| `src/Modules/Employee/Sense.Modules.Employee.Contracts/CardPdf/EmployeeCardPdfFormat.cs` (yeni) | Biçimleme, dönem ve tablo sınırı |
| `src/Modules/Employee/Sense.Modules.Employee.Application/CardPdf/EmployeeCardPdfComposer.cs` (yeni) | Doğrulama, toplama, PDF |
| `src/Modules/Employee/Sense.Modules.Employee.Application/CardPdf/EmployeeCardPdfQueries.cs` (yeni) | İki sorgu ve handler'ları |
| `src/Modules/Employee/Sense.Modules.Employee.Application/EmployeeErrors.cs` (değişir) | 4 hata kodu |
| `src/Modules/Employee/Sense.Modules.Employee.Api/Controllers/EmployeeDocumentsController.cs` (değişir) | 2 endpoint |
| `src/Modules/Employee/Sense.Modules.Employee.Infrastructure/CardPdf/EmployeeCardPdfSections.cs` (yeni) | Employee sağlayıcıları (6) |
| `src/Modules/Leave/Sense.Modules.Leave.Infrastructure/CardPdf/LeaveCardPdfSections.cs` (yeni) | leave, overtime |
| `src/Modules/Payroll/Sense.Modules.Payroll.Infrastructure/CardPdf/PayrollCardPdfSection.cs` (yeni) | payroll |
| `src/Modules/Assets/Sense.Modules.Assets.Infrastructure/CardPdf/AssetsCardPdfSection.cs` (yeni) | assets |
| `src/Modules/Documents/Sense.Modules.Documents.Infrastructure/CardPdf/DocumentsCardPdfSection.cs` (yeni) | documents |
| `src/Modules/Expense/Sense.Modules.Expense.Infrastructure/CardPdf/ExpenseCardPdfSection.cs` (yeni) | payments (Order 10) |
| `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource{,.en}.resx` (değişir) | Hata kodları, eksik enum etiketleri |
| `web/src/services/employees.service.ts` (değişir) | `downloadEmployeeCardPdf` |
| `web/src/components/employees/employee-card-pdf-dialog.tsx` (yeni) | Seçim diyaloğu |
| `web/src/components/employees/employee-card-pdf-dialog.test.tsx` (yeni) | Diyalog testi |
| `web/src/pages/employees/employee-card.tsx` (değişir) | PDF butonu |
| `web/public/locales/{tr,en,de}/employees.json` (değişir) | `card.pdf.*` anahtarları |

Bütün test sınıflarında ortak "etiketsiz localizer" kurulumu, test sınıfının içinde şu yardımcıyla yapılır (her testte yeniden yazılır):

```csharp
private static IStringLocalizer<SharedResource> Localizer()
{
    var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
    localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
    return localizer;
}
```

---

### Task 1: PDF yazıcısına bölüm bandı

**Files:**
- Modify: `src/Shared/Sense.Shared.Contracts/Export/ISimplePdfWriter.cs` (`SimplePdfSignatures` kaydının altına)
- Modify: `src/Shared/Sense.Shared.Infrastructure/Export/QuestPdfSimpleWriter.cs` (`Render` switch'i ve sabitler)
- Test: `tests/Modules/Sense.Shared.Infrastructure.Tests/Export/QuestPdfSimpleWriterTests.cs`

**Interfaces:**
- Produces: `public sealed record SimplePdfSectionHeading(string Text) : SimplePdfBlock;`

- [ ] **Step 1: Failing testi ekle.** Test `QuestPdfSimpleWriterTests` sınıfının sonuna eklenir. Assertion kütüphanesi (Shouldly ya da `Assert`) dosyanın mevcut üslubuna uyar.

```csharp
[Fact]
public void Write_WithSectionHeading_ProducesPdf()
{
    var document = new SimplePdfDocument(
        "Çalışan Kartı",
        null,
        [new SimplePdfSectionHeading("Genel bilgiler"), new SimplePdfParagraph("İçerik")]);

    var bytes = new QuestPdfSimpleWriter().Write(document);

    System.Text.Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("%PDF");
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `export PATH="/c/Program Files/dotnet:$PATH"; dotnet test tests/Modules/Sense.Shared.Infrastructure.Tests --filter "FullyQualifiedName~QuestPdfSimpleWriterTests"`
Expected: `SimplePdfSectionHeading` tanımsız olduğu için derleme hatası.

- [ ] **Step 3: Tipi ekle** (`ISimplePdfWriter.cs`, `SimplePdfSignatures` altına)

```csharp
/// <summary>A full-width shaded band that opens a section of a multi-section document (e.g. employee card PDF).</summary>
public sealed record SimplePdfSectionHeading(string Text) : SimplePdfBlock;
```

- [ ] **Step 4: Render'ı ekle** (`QuestPdfSimpleWriter.cs`)

Sabitlere:
```csharp
    private const float SectionFontSize = 12;
```

`Render` switch'ine, `default` satırından önce:
```csharp
            case SimplePdfSectionHeading section:
                container.PaddingTop(4).Background(HeaderFill).PaddingVertical(5).PaddingHorizontal(6)
                    .Text(section.Text).FontSize(SectionFontSize).Bold();
                break;
```

- [ ] **Step 5: Testi çalıştır.** Aynı komut. Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Shared/Sense.Shared.Contracts/Export/ISimplePdfWriter.cs src/Shared/Sense.Shared.Infrastructure/Export/QuestPdfSimpleWriter.cs tests/Modules/Sense.Shared.Infrastructure.Tests/Export/QuestPdfSimpleWriterTests.cs
git commit -m "feat(export): PDF yaziciya bolum bandi blogu"
```

---

### Task 2: Employee.Contracts sözleşmesi ve biçimleme yardımcısı

**Files:**
- Create: `src/Modules/Employee/Sense.Modules.Employee.Contracts/CardPdf/IEmployeeCardPdfSection.cs`
- Create: `src/Modules/Employee/Sense.Modules.Employee.Contracts/CardPdf/EmployeeCardPdfFormat.cs`
- Test: `tests/Modules/Sense.Modules.Employee.Tests/EmployeeCardPdfFormatTests.cs`

**Interfaces:**
- Produces (namespace `Sense.Modules.Employee.Contracts.CardPdf`):
  - `EmployeeCardPdfSectionKeys`: sabitler, `All`, `Title(string key)`
  - `EmployeeCardPdfRequest(Guid TenantId, Guid EmployeeId, bool IsOwnCard, DateOnly? From, DateOnly? To)`
  - `EmployeeCardPdfSectionResult(IReadOnlyList<SimplePdfBlock>? Blocks)`:
    - `static Unavailable`
    - `bool IsAvailable`
    - `static IReadOnlyList<SimplePdfBlock>? Part<T>(Result<T>, Func<T, IEnumerable<SimplePdfBlock>>)`
    - `static EmployeeCardPdfSectionResult Combine(params IReadOnlyList<SimplePdfBlock>?[])`
  - `IEmployeeCardPdfSection { string Key; int Order; string? ModuleKey; Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest, CancellationToken) }`
  - `EmployeeCardPdfFormat`: `MaxRowsPerTable`, `Empty`, `Date(DateOnly?)`, `Timestamp(DateTime?)`, `Amount(decimal?, string?)`, `Text(string?)`, `YesNo(bool)`, `InPeriod(DateOnly, DateOnly?, DateOnly?, DateOnly?)`, `PeriodText(DateOnly?, DateOnly?)`, `KeyValues(string?, params (string, string)[])`, `Table(string, IReadOnlyList<string>, IEnumerable<IReadOnlyList<string>>, IReadOnlyList<int>?)`

> Spec §3.1'deki soyut `Content`/`Unavailable` hiyerarşisi yerine tek bir record kullanılır: `Blocks == null` kapalı demektir. Davranış aynıdır.

- [ ] **Step 1: Failing testleri yaz**

```csharp
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

public sealed class EmployeeCardPdfFormatTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    [Fact]
    public void InPeriod_OpenEndedRecordStartingInsidePeriod_IsIncluded() =>
        EmployeeCardPdfFormat.InPeriod(new DateOnly(2026, 3, 1), null, From, To).ShouldBeTrue();

    [Fact]
    public void InPeriod_RecordEndingBeforePeriod_IsExcluded() =>
        EmployeeCardPdfFormat.InPeriod(new DateOnly(2025, 3, 1), new DateOnly(2025, 12, 31), From, To).ShouldBeFalse();

    [Fact]
    public void InPeriod_RecordStartingAfterPeriod_IsExcluded() =>
        EmployeeCardPdfFormat.InPeriod(new DateOnly(2026, 7, 1), null, From, To).ShouldBeFalse();

    [Fact]
    public void InPeriod_RecordSpanningPeriodStart_IsIncluded() =>
        EmployeeCardPdfFormat.InPeriod(new DateOnly(2025, 12, 20), new DateOnly(2026, 1, 5), From, To).ShouldBeTrue();

    [Fact]
    public void InPeriod_NoBounds_IsIncluded() =>
        EmployeeCardPdfFormat.InPeriod(new DateOnly(2001, 1, 1), null, null, null).ShouldBeTrue();

    [Fact]
    public void Table_OverLimit_TruncatesAndAddsNote()
    {
        var rows = Enumerable.Range(0, EmployeeCardPdfFormat.MaxRowsPerTable + 3)
            .Select(i => (IReadOnlyList<string>)[i.ToString()]);

        var blocks = EmployeeCardPdfFormat.Table("İzinler", ["No"], rows).ToList();

        blocks[0].ShouldBeOfType<SimplePdfTable>().Rows.Count.ShouldBe(EmployeeCardPdfFormat.MaxRowsPerTable);
        blocks[1].ShouldBeOfType<SimplePdfParagraph>().Text.ShouldBe("3 kayıt daha var; dönemi daraltın.");
    }

    [Fact]
    public void Table_NoRows_WritesEmptyLine() =>
        EmployeeCardPdfFormat.Table("İzinler", ["No"], []).ShouldHaveSingleItem()
            .ShouldBeOfType<SimplePdfParagraph>().Text.ShouldBe("İzinler: kayıt yok.");

    [Fact]
    public void Part_Forbidden_IsNull() =>
        EmployeeCardPdfSectionResult.Part(Result.Failure<int>(Error.Forbidden("auth.forbidden")), _ => []).ShouldBeNull();

    [Fact]
    public void Part_NotFound_IsNull() =>
        EmployeeCardPdfSectionResult.Part(Result.Failure<int>(Error.NotFound("x")), _ => []).ShouldBeNull();

    [Fact]
    public void Part_Failure_Throws() =>
        Should.Throw<InvalidOperationException>(() =>
            EmployeeCardPdfSectionResult.Part(Result.Failure<int>(Error.Failure("x")), _ => []));

    [Fact]
    public void Combine_AllPartsNull_IsUnavailable() =>
        EmployeeCardPdfSectionResult.Combine(null, null).IsAvailable.ShouldBeFalse();

    [Fact]
    public void Combine_OnePartAvailable_KeepsItsBlocks() =>
        EmployeeCardPdfSectionResult.Combine(null, [new SimplePdfParagraph("a")]).Blocks!.Count.ShouldBe(1);
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `export PATH="/c/Program Files/dotnet:$PATH"; dotnet test tests/Modules/Sense.Modules.Employee.Tests --filter "FullyQualifiedName~EmployeeCardPdfFormatTests"`
Expected: `Sense.Modules.Employee.Contracts.CardPdf` bulunamadığı için derleme hatası.

- [ ] **Step 3: Sözleşme** (`IEmployeeCardPdfSection.cs`)

```csharp
using Sense.Shared.Contracts.Export;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Contracts.CardPdf;

/// <summary>Employee card tab keys (same as the web card's URL segments), in PDF display order.</summary>
public static class EmployeeCardPdfSectionKeys
{
    public const string General = "general";
    public const string Personal = "personal";
    public const string CustomFields = "customFields";
    public const string Career = "career";
    public const string Payments = "payments";
    public const string Payroll = "payroll";
    public const string Documents = "documents";
    public const string Assets = "assets";
    public const string Leave = "leave";
    public const string Overtime = "overtime";
    public const string Account = "account";

    public static readonly IReadOnlyList<string> All =
        [General, Personal, CustomFields, Career, Payments, Payroll, Documents, Assets, Leave, Overtime, Account];

    /// <summary>Turkish section title printed on the section band.</summary>
    public static string Title(string key) => key switch
    {
        General => "Genel Bilgiler",
        Personal => "Kişisel Bilgiler",
        CustomFields => "Özel Alanlar",
        Career => "Kariyer",
        Payments => "Ödemeler",
        Payroll => "Bordro",
        Documents => "Evraklar",
        Assets => "Zimmetler",
        Leave => "İzinler",
        Overtime => "Fazla Mesai",
        Account => "Hesap",
        _ => key,
    };
}

/// <summary>
/// One card PDF request. <paramref name="IsOwnCard"/>: the caller is the employee (self-service), so providers use the
/// module's "my" queries. <paramref name="From"/>/<paramref name="To"/> filter event-type lists only; null = unbounded.
/// </summary>
public sealed record EmployeeCardPdfRequest(Guid TenantId, Guid EmployeeId, bool IsOwnCard, DateOnly? From, DateOnly? To);

/// <summary>A provider's output: blocks to print, or <c>null</c> blocks when the caller may not see this section.</summary>
public sealed record EmployeeCardPdfSectionResult(IReadOnlyList<SimplePdfBlock>? Blocks)
{
    public static readonly EmployeeCardPdfSectionResult Unavailable = new((IReadOnlyList<SimplePdfBlock>?)null);

    public bool IsAvailable => Blocks is not null;

    /// <summary>
    /// Maps one query result to a part: success → blocks, Forbidden/NotFound → null (hidden from this caller);
    /// any other failure throws so the whole PDF fails instead of silently missing data.
    /// </summary>
    public static IReadOnlyList<SimplePdfBlock>? Part<T>(Result<T> result, Func<T, IEnumerable<SimplePdfBlock>> map)
    {
        if (result.IsSuccess)
        {
            return map(result.Value).ToList();
        }

        return result.Error.Type is ErrorType.Forbidden or ErrorType.NotFound
            ? null
            : throw new InvalidOperationException($"Employee card PDF section query failed: {result.Error.Code}");
    }

    /// <summary>Unavailable only when every part is hidden; otherwise the visible parts in order.</summary>
    public static EmployeeCardPdfSectionResult Combine(params IReadOnlyList<SimplePdfBlock>?[] parts) =>
        parts.All(part => part is null)
            ? Unavailable
            : new EmployeeCardPdfSectionResult(parts.Where(part => part is not null).SelectMany(part => part!).ToList());
}

/// <summary>
/// Contributes one employee card tab to the card PDF. Implemented by the module that owns the tab's data (in its
/// Infrastructure) and registered in its module; Employee collects every registration. Providers sharing a
/// <see cref="Key"/> are joined by <see cref="Order"/>.
/// </summary>
public interface IEmployeeCardPdfSection
{
    string Key { get; }

    int Order { get; }

    /// <summary>ProductModuleKeys value the provider needs enabled; null for core modules.</summary>
    string? ModuleKey { get; }

    Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Biçimleme yardımcısı** (`EmployeeCardPdfFormat.cs`)

```csharp
using System.Globalization;
using Sense.Shared.Contracts.Export;

namespace Sense.Modules.Employee.Contracts.CardPdf;

/// <summary>Shared tr-TR formatting and table rules for every card PDF provider.</summary>
public static class EmployeeCardPdfFormat
{
    public const int MaxRowsPerTable = 500;
    public const string Empty = "—";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Date(DateOnly? value) =>
        value?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? Empty;

    // Not named "DateTime": a member with that name would shadow the System.DateTime type inside this class.
    public static string Timestamp(System.DateTime? value) =>
        value is { } v ? Date(DateOnly.FromDateTime(v)) : Empty;

    public static string Amount(decimal? value, string? currency = null) =>
        value is not { } v ? Empty : currency is null ? v.ToString("N2", Turkish) : $"{v.ToString("N2", Turkish)} {currency}";

    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? Empty : value;

    public static string YesNo(bool value) => value ? "Evet" : "Hayır";

    /// <summary>Record [start, end] overlaps [from, to]; null end = single-day record; null bound = unbounded.</summary>
    public static bool InPeriod(DateOnly start, DateOnly? end, DateOnly? from, DateOnly? to) =>
        (to is null || start <= to) && (from is null || (end ?? start) >= from);

    public static string PeriodText(DateOnly? from, DateOnly? to) =>
        from is null && to is null ? "Tüm zamanlar" : $"{Date(from)} – {Date(to)}";

    public static SimplePdfKeyValues KeyValues(string? heading, params (string Label, string Value)[] rows) =>
        new(heading, rows.Select(row => new KeyValuePair<string, string>(row.Label, row.Value)).ToList());

    /// <summary>A table capped at <see cref="MaxRowsPerTable"/> rows (caller orders newest first) plus a note for the rest.</summary>
    public static IEnumerable<SimplePdfBlock> Table(
        string heading,
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<string>> rows,
        IReadOnlyList<int>? rightAlignedColumns = null)
    {
        var all = rows.ToList();
        if (all.Count == 0)
        {
            return [new SimplePdfParagraph($"{heading}: kayıt yok.")];
        }

        var table = new SimplePdfTable(heading, columns, all.Take(MaxRowsPerTable).ToList(), rightAlignedColumns);
        return all.Count > MaxRowsPerTable
            ? [table, new SimplePdfParagraph($"{all.Count - MaxRowsPerTable} kayıt daha var; dönemi daraltın.")]
            : [table];
    }
}
```

`Sense.Modules.Employee.Contracts.csproj` projesinde `Sense.Shared.Kernel` referansı yoksa, mevcut ProjectReference'ların biçimiyle eklenir.

- [ ] **Step 5: Testleri çalıştır.** Aynı komut. Expected: 12 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Employee/Sense.Modules.Employee.Contracts/CardPdf tests/Modules/Sense.Modules.Employee.Tests/EmployeeCardPdfFormatTests.cs
git commit -m "feat(employee): kart PDF bolum sozlesmesi ve bicim yardimcisi"
```

(csproj değiştiyse `git add`'e eklenir.)

---

### Task 3: Composer, sorgular, endpoint'ler, hata kodları

**Files:**
- Create: `src/Modules/Employee/Sense.Modules.Employee.Application/CardPdf/EmployeeCardPdfComposer.cs`
- Create: `src/Modules/Employee/Sense.Modules.Employee.Application/CardPdf/EmployeeCardPdfQueries.cs`
- Modify: `src/Modules/Employee/Sense.Modules.Employee.Application/EmployeeErrors.cs` (sınıf sonuna 4 sabit)
- Modify: `src/Modules/Employee/Sense.Modules.Employee.Api/Controllers/EmployeeDocumentsController.cs`
- Modify: `src/Modules/Employee/Sense.Modules.Employee.Api/EmployeeModule.cs` (`services.AddScoped<EmployeeDetailBuilder>();` altına)
- Modify: `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx` ve `SharedResource.en.resx` (`employee.not_found` satırının altına)
- Test: `tests/Modules/Sense.Modules.Employee.Tests/EmployeeCardPdfComposerTests.cs`

**Interfaces:**
- Consumes: Task 2; `IModuleAccessService.IsEnabledAsync(string, CancellationToken)`; `ISimplePdfWriter.Write(SimplePdfDocument)`; `IEmployeeReadStore.GetAsync(Guid, CancellationToken) : Task<EmployeeRow?>` (`EmployeeRow.Id`, `EmployeeRow.DisplayName`); `SelfEmployeeResolver.ResolveAsync(CancellationToken) : Task<Guid?>`.
- Produces:
  - `EmployeeCardPdfComposer.ComposeAsync(EmployeeCardPdfRequest request, string employeeName, IReadOnlyCollection<string> sectionKeys, CancellationToken ct) : Task<Result<ExportFileResult>>`
  - `GetEmployeeCardPdfQuery(Guid Id, IReadOnlyList<string> Sections, DateOnly? From, DateOnly? To) : IQuery<ExportFileResult>, IScopedRequest`
  - `GetMyEmployeeCardPdfQuery(IReadOnlyList<string> Sections, DateOnly? From, DateOnly? To) : IQuery<ExportFileResult>`
  - HTTP: `GET api/v1/employees/{id}/card-pdf?sections=a,b&from=yyyy-MM-dd&to=yyyy-MM-dd` ve `GET api/v1/employees/me/card-pdf?...`

- [ ] **Step 1: Failing testleri yaz**

```csharp
using NSubstitute;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.CardPdf;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Export;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

public sealed class EmployeeCardPdfComposerTests
{
    private static readonly EmployeeCardPdfRequest Request =
        new(Guid.NewGuid(), Guid.NewGuid(), false, new DateOnly(2025, 9, 28), new DateOnly(2026, 9, 28));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeSection(string key, int order, string? module, EmployeeCardPdfSectionResult result) : IEmployeeCardPdfSection
    {
        public int Calls { get; private set; }

        public string Key => key;

        public int Order => order;

        public string? ModuleKey => module;

        public Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static EmployeeCardPdfSectionResult Text(string text) => new([new SimplePdfParagraph(text)]);

    private static FakeSection Fake(string key, string text) => new(key, 0, null, Text(text));

    private static (EmployeeCardPdfComposer Composer, Func<SimplePdfDocument?> Document) Composer(
        IEnumerable<IEmployeeCardPdfSection> sections, params string[] disabledModules)
    {
        var modules = Substitute.For<IModuleAccessService>();
        modules.IsEnabledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(!disabledModules.Contains(call.Arg<string>())));
        SimplePdfDocument? captured = null;
        var writer = Substitute.For<ISimplePdfWriter>();
        writer.Write(Arg.Do<SimplePdfDocument>(d => captured = d)).Returns([1]);
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero));
        return (new EmployeeCardPdfComposer(sections, modules, writer, clock), () => captured);
    }

    private static List<string> Headings(SimplePdfDocument document) =>
        document.Blocks.OfType<SimplePdfSectionHeading>().Select(h => h.Text).ToList();

    [Fact]
    public void GetEmployeeCardPdfQuery_IsScopedToTargetEmployee()
    {
        var id = Guid.NewGuid();
        IScopedRequest query = new GetEmployeeCardPdfQuery(id, ["general"], null, null);

        query.TargetEmployeeId.ShouldBe(id);
        typeof(GetEmployeeCardPdfQuery).GetCustomAttributes(typeof(RequiresPermissionAttribute), false)
            .Cast<RequiresPermissionAttribute>().ShouldHaveSingleItem().Permission.ShouldBe(EmployeePermissions.EmployeesReadKey);
    }

    [Fact]
    public async Task Compose_OrdersSectionsByCardOrder_NotRequestOrder()
    {
        var (composer, document) = Composer([Fake("leave", "İzin"), Fake("general", "Genel")]);

        var result = await composer.ComposeAsync(Request, "Ali Veli", ["leave", "general"], CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Headings(document()!).ShouldBe(["Genel Bilgiler", "İzinler"]);
    }

    [Fact]
    public async Task Compose_OmitsSectionsNotRequested()
    {
        var (composer, document) = Composer([Fake("leave", "İzin"), Fake("general", "Genel")]);

        await composer.ComposeAsync(Request, "Ali Veli", ["general"], CancellationToken.None);

        Headings(document()!).ShouldBe(["Genel Bilgiler"]);
    }

    [Fact]
    public async Task Compose_DropsUnavailableSection()
    {
        var hidden = new FakeSection("payroll", 0, null, EmployeeCardPdfSectionResult.Unavailable);
        var (composer, document) = Composer([Fake("general", "Genel"), hidden]);

        await composer.ComposeAsync(Request, "Ali Veli", ["general", "payroll"], CancellationToken.None);

        Headings(document()!).ShouldBe(["Genel Bilgiler"]);
    }

    [Fact]
    public async Task Compose_WhenModuleDisabled_SkipsProviderWithoutCallingIt()
    {
        var leave = new FakeSection("leave", 0, ProductModuleKeys.Leave, Text("İzin"));
        var (composer, document) = Composer([Fake("general", "Genel"), leave], ProductModuleKeys.Leave);

        await composer.ComposeAsync(Request, "Ali Veli", ["general", "leave"], CancellationToken.None);

        leave.Calls.ShouldBe(0);
        Headings(document()!).ShouldBe(["Genel Bilgiler"]);
    }

    [Fact]
    public async Task Compose_JoinsProvidersOfSameKeyByOrder()
    {
        var (composer, document) = Composer(
            [new FakeSection("payments", 10, null, Text("B")), new FakeSection("payments", 0, null, Text("A"))]);

        await composer.ComposeAsync(Request, "Ali Veli", ["payments"], CancellationToken.None);

        var texts = document()!.Blocks.OfType<SimplePdfParagraph>().Select(p => p.Text).ToList();
        texts.IndexOf("A").ShouldBeLessThan(texts.IndexOf("B"));
    }

    [Fact]
    public async Task Compose_EmptySection_PrintsNoRecordLine()
    {
        var (composer, document) = Composer([new FakeSection("general", 0, null, new EmployeeCardPdfSectionResult([]))]);

        await composer.ComposeAsync(Request, "Ali Veli", ["general"], CancellationToken.None);

        document()!.Blocks.OfType<SimplePdfParagraph>().ShouldContain(p => p.Text == "Bu bölümde kayıt yok.");
    }

    [Fact]
    public async Task Compose_WhenEverySectionIsUnavailable_IsForbidden()
    {
        var (composer, _) = Composer([new FakeSection("payroll", 0, null, EmployeeCardPdfSectionResult.Unavailable)]);

        var result = await composer.ComposeAsync(Request, "Ali Veli", ["payroll"], CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Code.ShouldBe(EmployeeErrors.CardPdfNoSections);
    }

    [Fact]
    public async Task Compose_NoSections_IsValidationError()
    {
        var (composer, _) = Composer([Fake("general", "Genel")]);

        var result = await composer.ComposeAsync(Request, "Ali Veli", [], CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeErrors.CardPdfSectionsRequired);
    }

    [Fact]
    public async Task Compose_UnknownSection_IsValidationError()
    {
        var (composer, _) = Composer([Fake("general", "Genel")]);

        var result = await composer.ComposeAsync(Request, "Ali Veli", ["general", "other"], CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeErrors.CardPdfUnknownSection);
    }

    [Fact]
    public async Task Compose_FromAfterTo_IsValidationError()
    {
        var (composer, _) = Composer([Fake("general", "Genel")]);
        var reversed = Request with { From = new DateOnly(2026, 9, 28), To = new DateOnly(2026, 1, 1) };

        var result = await composer.ComposeAsync(reversed, "Ali Veli", ["general"], CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeErrors.CardPdfPeriodInvalid);
    }

    [Fact]
    public async Task Compose_FileName_IsTurkishSlugWithDate()
    {
        var (composer, _) = Composer([Fake("general", "Genel")]);

        var result = await composer.ComposeAsync(Request, "Şule Çağlar", ["general"], CancellationToken.None);

        result.Value.FileName.ShouldBe("calisan-karti-sule-caglar-2026-09-28.pdf");
        result.Value.ContentType.ShouldBe("application/pdf");
    }

    [Fact]
    public async Task Compose_SummaryShowsNameAndPeriod()
    {
        var (composer, document) = Composer([Fake("general", "Genel")]);

        await composer.ComposeAsync(Request, "Ali Veli", ["general"], CancellationToken.None);

        var summary = document()!.Blocks[0].ShouldBeOfType<SimplePdfKeyValues>();
        summary.Rows.ShouldContain(r => r.Key == "Çalışan" && r.Value == "Ali Veli");
        summary.Rows.ShouldContain(r => r.Key == "Dönem" && r.Value == "28.09.2025 – 28.09.2026");
    }

    [Fact]
    public async Task Compose_WithRealWriter_ProducesPdfBytes()
    {
        var modules = Substitute.For<IModuleAccessService>();
        modules.IsEnabledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var composer = new EmployeeCardPdfComposer([Fake("general", "Genel")], modules, new QuestPdfSimpleWriter(), TimeProvider.System);

        var result = await composer.ComposeAsync(Request, "Ali Veli", ["general"], CancellationToken.None);

        System.Text.Encoding.ASCII.GetString(result.Value.Content, 0, 4).ShouldBe("%PDF");
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `export PATH="/c/Program Files/dotnet:$PATH"; dotnet test tests/Modules/Sense.Modules.Employee.Tests --filter "FullyQualifiedName~EmployeeCardPdfComposerTests"`
Expected: `EmployeeCardPdfComposer` bulunamadığı için derleme hatası.

- [ ] **Step 3: Hata kodları**

`EmployeeErrors.cs` sınıfının sonuna:
```csharp
    public const string CardPdfSectionsRequired = "employee.card_pdf.sections_required";
    public const string CardPdfUnknownSection = "employee.card_pdf.unknown_section";
    public const string CardPdfPeriodInvalid = "employee.card_pdf.period_invalid";
    public const string CardPdfNoSections = "employee.card_pdf.no_sections";
```

`SharedResource.resx` içinde `employee.not_found` altına:
```xml
  <data name="employee.card_pdf.sections_required" xml:space="preserve"><value>PDF için en az bir bölüm seçin.</value></data>
  <data name="employee.card_pdf.unknown_section" xml:space="preserve"><value>Bilinmeyen kart bölümü: {section}.</value></data>
  <data name="employee.card_pdf.period_invalid" xml:space="preserve"><value>Dönem başlangıcı bitişinden sonra olamaz.</value></data>
  <data name="employee.card_pdf.no_sections" xml:space="preserve"><value>Seçilen bölümlerin hiçbirini görüntüleme yetkiniz yok.</value></data>
```

`SharedResource.en.resx` içinde `employee.not_found` altına:
```xml
  <data name="employee.card_pdf.sections_required" xml:space="preserve"><value>Select at least one section for the PDF.</value></data>
  <data name="employee.card_pdf.unknown_section" xml:space="preserve"><value>Unknown card section: {section}.</value></data>
  <data name="employee.card_pdf.period_invalid" xml:space="preserve"><value>The period start cannot be after its end.</value></data>
  <data name="employee.card_pdf.no_sections" xml:space="preserve"><value>You are not allowed to view any of the selected sections.</value></data>
```

- [ ] **Step 4: Composer** (`EmployeeCardPdfComposer.cs`)

```csharp
using System.Globalization;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.CardPdf;

/// <summary>
/// Builds the employee card PDF from every registered <see cref="IEmployeeCardPdfSection"/>: validates the request,
/// skips providers of disabled modules, drops sections the caller may not see, and renders the rest in card order.
/// </summary>
public sealed class EmployeeCardPdfComposer(
    IEnumerable<IEmployeeCardPdfSection> sections,
    IModuleAccessService modules,
    ISimplePdfWriter writer,
    TimeProvider clock)
{
    private const string EmptySectionText = "Bu bölümde kayıt yok.";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public async Task<Result<ExportFileResult>> ComposeAsync(
        EmployeeCardPdfRequest request,
        string employeeName,
        IReadOnlyCollection<string> sectionKeys,
        CancellationToken cancellationToken)
    {
        if (Validate(request, sectionKeys) is { } error)
        {
            return error;
        }

        var blocks = new List<SimplePdfBlock>
        {
            EmployeeCardPdfFormat.KeyValues(
                null,
                ("Çalışan", employeeName),
                ("Dönem", EmployeeCardPdfFormat.PeriodText(request.From, request.To))),
        };
        var anySection = false;
        foreach (var key in EmployeeCardPdfSectionKeys.All.Where(sectionKeys.Contains))
        {
            var sectionBlocks = await BuildSectionAsync(key, request, cancellationToken);
            if (sectionBlocks is null)
            {
                continue;
            }

            anySection = true;
            blocks.Add(new SimplePdfSectionHeading(EmployeeCardPdfSectionKeys.Title(key)));
            blocks.AddRange(sectionBlocks.Count == 0 ? [new SimplePdfParagraph(EmptySectionText)] : sectionBlocks);
        }

        if (!anySection)
        {
            return Error.Forbidden(EmployeeErrors.CardPdfNoSections);
        }

        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Istanbul);
        var document = new SimplePdfDocument(
            "Çalışan Kartı",
            employeeName,
            blocks,
            $"Oluşturma: {now.ToString("dd.MM.yyyy HH:mm", Turkish)}");
        var fileName = $"calisan-karti-{Slug(employeeName)}-{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf";
        return new ExportFileResult(fileName, "application/pdf", writer.Write(document));
    }

    private static Error? Validate(EmployeeCardPdfRequest request, IReadOnlyCollection<string> sectionKeys)
    {
        if (sectionKeys.Count == 0)
        {
            return Error.Validation(EmployeeErrors.CardPdfSectionsRequired);
        }

        var unknown = sectionKeys.FirstOrDefault(key => !EmployeeCardPdfSectionKeys.All.Contains(key));
        if (unknown is not null)
        {
            return Error.Validation(EmployeeErrors.CardPdfUnknownSection, ("section", unknown));
        }

        return request.From is { } from && request.To is { } to && from > to
            ? Error.Validation(EmployeeErrors.CardPdfPeriodInvalid)
            : null;
    }

    private async Task<IReadOnlyList<SimplePdfBlock>?> BuildSectionAsync(
        string key, EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        List<SimplePdfBlock>? result = null;
        foreach (var provider in sections.Where(s => s.Key == key).OrderBy(s => s.Order))
        {
            if (provider.ModuleKey is { } module && !await modules.IsEnabledAsync(module, cancellationToken))
            {
                continue;
            }

            var built = await provider.BuildAsync(request, cancellationToken);
            if (built.Blocks is { } provided)
            {
                (result ??= []).AddRange(provided);
            }
        }

        return result;
    }

    private static string Slug(string value)
    {
        var ascii = value.ToLower(Turkish)
            .Replace('ç', 'c').Replace('ğ', 'g').Replace('ı', 'i').Replace('ö', 'o').Replace('ş', 's').Replace('ü', 'u');
        var chars = ascii.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
```

- [ ] **Step 5: Sorgular** (`EmployeeCardPdfQueries.cs`)

```csharp
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.CardPdf;

/// <summary>
/// Employee card as PDF (selected tabs + period). Scoped on employees.read, which also guards section queries that are
/// not scoped themselves (assets, payroll); every section still needs its own permission.
/// </summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
public sealed record GetEmployeeCardPdfQuery(Guid Id, IReadOnlyList<string> Sections, DateOnly? From, DateOnly? To)
    : IQuery<ExportFileResult>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;
}

public sealed class GetEmployeeCardPdfHandler(ITenantContext tenant, IEmployeeReadStore store, EmployeeCardPdfComposer composer)
    : IQueryHandler<GetEmployeeCardPdfQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(GetEmployeeCardPdfQuery query, CancellationToken cancellationToken)
    {
        var row = await store.GetAsync(query.Id, cancellationToken);
        if (row is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        var request = new EmployeeCardPdfRequest(tenant.TenantId, row.Id, IsOwnCard: false, query.From, query.To);
        return await composer.ComposeAsync(request, row.DisplayName, query.Sections, cancellationToken);
    }
}

/// <summary>The caller's own card as PDF (self-service); sections use the modules' "my" queries.</summary>
[RequiresPermission(EmployeePermissions.MyViewKey)]
public sealed record GetMyEmployeeCardPdfQuery(IReadOnlyList<string> Sections, DateOnly? From, DateOnly? To) : IQuery<ExportFileResult>;

public sealed class GetMyEmployeeCardPdfHandler(
    ITenantContext tenant, SelfEmployeeResolver self, IEmployeeReadStore store, EmployeeCardPdfComposer composer)
    : IQueryHandler<GetMyEmployeeCardPdfQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(GetMyEmployeeCardPdfQuery query, CancellationToken cancellationToken)
    {
        var row = await self.ResolveAsync(cancellationToken) is { } id ? await store.GetAsync(id, cancellationToken) : null;
        if (row is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        var request = new EmployeeCardPdfRequest(tenant.TenantId, row.Id, IsOwnCard: true, query.From, query.To);
        return await composer.ComposeAsync(request, row.DisplayName, query.Sections, cancellationToken);
    }
}
```

- [ ] **Step 6: Endpoint'ler** (`EmployeeDocumentsController.cs`, `WorkCertificate` metodunun altına)

```csharp
    /// <summary>Employee card as PDF: comma-separated tab keys, optional period (yyyy-MM-dd) for event lists.</summary>
    [HttpGet(ApiRoutes.IdParam + "/card-pdf")]
    public async Task<IActionResult> CardPdf(
        Guid id, [FromQuery] string? sections, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        ToFile(await Dispatcher.Query(new GetEmployeeCardPdfQuery(id, SplitSections(sections), from, to), ct));

    /// <summary>The caller's own card as PDF (self-service).</summary>
    [HttpGet("me/card-pdf")]
    public async Task<IActionResult> MyCardPdf(
        [FromQuery] string? sections, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        ToFile(await Dispatcher.Query(new GetMyEmployeeCardPdfQuery(SplitSections(sections), from, to), ct));

    private static IReadOnlyList<string> SplitSections(string? sections) =>
        (sections ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private IActionResult ToFile(Result<ExportFileResult> result) =>
        result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
```

Eklenecek using'ler: `Sense.Modules.Employee.Application.CardPdf`, `Sense.Shared.Contracts.Export`, `Sense.Shared.Kernel.Results`.

- [ ] **Step 7: DI** (`EmployeeModule.cs`, `services.AddScoped<EmployeeDetailBuilder>();` altına)

```csharp
        services.AddScoped<Application.CardPdf.EmployeeCardPdfComposer>();
```

Handler'lar `AddModuleHandlers` taramasıyla kaydolur. `TimeProvider` zaten kayıtlıdır (`EmployeeDetailBuilder` onu kullanıyor).

- [ ] **Step 8: Testleri ve derlemeyi çalıştır.** Aynı test komutu, ardından `dotnet build src/Modules/Employee/Sense.Modules.Employee.Api`. Expected: 14 PASS, build başarılı.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Employee/Sense.Modules.Employee.Application/CardPdf src/Modules/Employee/Sense.Modules.Employee.Application/EmployeeErrors.cs src/Modules/Employee/Sense.Modules.Employee.Api/Controllers/EmployeeDocumentsController.cs src/Modules/Employee/Sense.Modules.Employee.Api/EmployeeModule.cs src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.en.resx tests/Modules/Sense.Modules.Employee.Tests/EmployeeCardPdfComposerTests.cs
git commit -m "feat(employee): calisan karti PDF endpoint'i ve bolum birlestirici"
```

---

### Task 4: Employee sağlayıcıları (general, personal, customFields, career, payments, account)

**Files:**
- Create: `src/Modules/Employee/Sense.Modules.Employee.Infrastructure/CardPdf/EmployeeCardPdfSections.cs`. Altı küçük sınıf ortak bir tabanı paylaştığı için tek dosyadadır, yaklaşık 250 satır.
- Modify: `src/Modules/Employee/Sense.Modules.Employee.Api/EmployeeModule.cs`
- Modify: `SharedResource{,.en}.resx` (eksik enum etiketleri)
- Test: `tests/Modules/Sense.Modules.Employee.Tests/EmployeeCardPdfSectionsTests.cs`

**Interfaces:**
- Consumes:
  - Task 2 tipleri ve `IDispatcher`.
  - Genel sorgular: `GetEmployeeQuery(Guid Id)`, `GetMyEmployeeQuery()` → `EmployeeDetailDto`.
  - Kariyer: `GetEmployeeCareerQuery(Guid)`, `GetMyCareerQuery()` → `IReadOnlyList<CareerRecordDto>`; `GetEmploymentHistoryQuery(Guid)` → `EmploymentHistoryDto`.
  - Ödemeler: `GetEmployeeSalariesQuery(Guid)`, `GetMySalariesQuery()` → `IReadOnlyList<SalaryRecordDto>`; `GetEmployeeBankAccountsQuery(Guid)`, `GetMyBankAccountsQuery()` → `IReadOnlyList<BankAccountDto>`.
  - Sendika: `ListUnionMembershipsQuery(Guid)` → `IReadOnlyList<UnionMembershipDto>`.
  - Etiketler: `IStringLocalizer<SharedResource>` (namespace `Sense.Shared.Infrastructure`) ve `ReportText.EnumText<TEnum>(TEnum?)` / `EnumText(string enumName, string? member)` (namespace `Sense.Shared.Infrastructure.Export`).
- Produces: `GeneralCardPdfSection`, `PersonalCardPdfSection`, `CustomFieldsCardPdfSection`, `CareerCardPdfSection`, `EmployeePaymentsCardPdfSection` (Order 0), `AccountCardPdfSection`.

- [ ] **Step 1: Failing testleri yaz**

```csharp
using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Employee.Infrastructure.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

public sealed class EmployeeCardPdfSectionsTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly EmployeeCardPdfRequest AdminRequest = new(Guid.NewGuid(), EmployeeId, false, null, null);
    private static readonly EmployeeCardPdfRequest OwnRequest = AdminRequest with { IsOwnCard = true };

    private static IStringLocalizer<SharedResource> Localizer()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        return localizer;
    }

    [Fact]
    public async Task General_WhenEmployeeQueryForbidden_IsUnavailable()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<EmployeeDetailDto>(Error.Forbidden("auth.forbidden"))));

        var result = await new GeneralCardPdfSection(dispatcher, Localizer()).BuildAsync(AdminRequest, CancellationToken.None);

        result.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task Career_OwnCard_UsesMyQueryAndSkipsEmploymentHistory()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetMyCareerQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<CareerRecordDto>>([])));

        var result = await new CareerCardPdfSection(dispatcher, Localizer()).BuildAsync(OwnRequest, CancellationToken.None);

        result.IsAvailable.ShouldBeTrue();
        await dispatcher.DidNotReceive().Query(Arg.Any<GetEmployeeCareerQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Payments_SalaryForbiddenBankAllowed_ShowsBankOnly()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeSalariesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<IReadOnlyList<SalaryRecordDto>>(Error.Forbidden("auth.forbidden"))));
        dispatcher.Query(Arg.Any<GetEmployeeBankAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<BankAccountDto>>([])));

        var result = await new EmployeePaymentsCardPdfSection(dispatcher, Localizer()).BuildAsync(AdminRequest, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfParagraph>().ShouldHaveSingleItem().Text.ShouldBe("Banka hesapları: kayıt yok.");
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `export PATH="/c/Program Files/dotnet:$PATH"; dotnet test tests/Modules/Sense.Modules.Employee.Tests --filter "FullyQualifiedName~EmployeeCardPdfSectionsTests"`
Expected: `Sense.Modules.Employee.Infrastructure.CardPdf` yok diye derleme hatası.

- [ ] **Step 3: Sağlayıcılar** (`EmployeeCardPdfSections.cs`)

Enum tiplerinin namespace'leri derleyicinin istediği `using`'lerle eklenir:
- `Sense.Modules.Employee.Domain.*`: `Gender`, `MaritalStatus`, `EducationStatus`, `HighestEducationLevel`, `MilitaryStatus`, `DisabilityDegree`, `BloodType`, `ContractType`, `EmploymentType`, `CareerChangeType`, `SalaryType`, `PayFrequency`, `BankAccountType`, `UnionCoverageType`
- `Sense.Modules.Identity.Contracts`: `UserAccountStatus`

Bulmak için: `grep -rn "enum CareerChangeType" src/Modules/Employee`.

```csharp
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.EmploymentPeriods;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Application.Union;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using Sense.Shared.Kernel.Results;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Employee.Infrastructure.CardPdf;

/// <summary>Base for the Employee-owned card sections: detail lookup (own card → /me query) and enum labels.</summary>
public abstract class EmployeeOwnedCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    protected IDispatcher Dispatcher { get; } = dispatcher;

    public abstract string Key { get; }

    public int Order => 0;

    public string? ModuleKey => null;

    public abstract Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken);

    protected Task<Result<EmployeeDetailDto>> DetailAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken) =>
        request.IsOwnCard
            ? Dispatcher.Query(new GetMyEmployeeQuery(), cancellationToken)
            : Dispatcher.Query(new GetEmployeeQuery(request.EmployeeId), cancellationToken);

    protected string Label<TEnum>(TEnum? value) where TEnum : struct, Enum => localizer.EnumText(value) ?? Empty;

    protected string Label(string enumName, string? member) => localizer.EnumText(enumName, member) ?? Text(member);
}

public sealed class GeneralCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.General;

    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken) =>
        EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(await DetailAsync(request, cancellationToken), e =>
        [
            KeyValues(
                "İş bilgileri",
                ("Ad soyad", e.DisplayName),
                ("Durum", Label("EmployeeStatus", e.Status)),
                ("Departman", Text(e.DepartmentName)),
                ("Unvan", Text(e.TitleName)),
                ("Yönetici", Text(e.ManagerDisplayName)),
                ("İşe giriş", Date(e.HireDate)),
                ("Pozisyon başlangıcı", Date(e.PositionStartDate)),
                ("İzin hak ediş başlangıcı", Date(e.LeaveAccrualStartDate)),
                ("Sözleşme tipi", Label(e.ContractType)),
                ("Çalışma tipi", Label(e.EmploymentType)),
                ("İş e-postası", Text(e.WorkEmail)),
                ("İş telefonu", Text(e.WorkPhone)),
                ("İşten çıkış", Date(e.TerminationDate))),
            KeyValues("Alt işveren", ("Alt işveren", Text(e.SubcontractorName))),
        ]));
}

public sealed class PersonalCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.Personal;

    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        // NationalId arrives masked and the special-category fields null without employee.sensitive.view (EmployeeDetailBuilder).
        var personal = EmployeeCardPdfSectionResult.Part(await DetailAsync(request, cancellationToken), e =>
        [
            KeyValues(
                "Kişisel bilgiler",
                ("T.C. kimlik no", Text(e.NationalId)),
                ("Doğum tarihi", Date(e.BirthDate)),
                ("Cinsiyet", Label<Gender>(e.Gender)),
                ("Medeni durum", Label<MaritalStatus>(e.MaritalStatus)),
                ("Çocuk sayısı", e.ChildCount?.ToString() ?? Empty),
                ("Eğitim durumu", Label(e.EducationStatus)),
                ("En yüksek eğitim", Label(e.HighestEducationLevel)),
                ("Askerlik durumu", Label(e.MilitaryStatus)),
                ("Engellilik derecesi", Label(e.DisabilityDegree)),
                ("Kan grubu", Label(e.BloodType))),
            KeyValues(
                "İletişim",
                ("Kişisel e-posta", Text(e.PersonalEmail)),
                ("Cep telefonu", Text(e.MobilePhone)),
                ("Ev telefonu", Text(e.HomePhone)),
                ("Adres", Text(e.AddressLine)),
                ("Şehir", Text(e.City)),
                ("Posta kodu", Text(e.PostalCode))),
        ]);
        if (personal is null)
        {
            return EmployeeCardPdfSectionResult.Unavailable;
        }

        // Union memberships have no self-service query; the own card omits them.
        var union = request.IsOwnCard
            ? null
            : EmployeeCardPdfSectionResult.Part(
                await Dispatcher.Query(new ListUnionMembershipsQuery(request.EmployeeId), cancellationToken),
                rows => Table(
                    "Sendika üyelikleri",
                    ["Sendika", "Üyelik tarihi", "Kapsam", "Temsilci", "Bitiş"],
                    rows.OrderByDescending(r => r.MembershipDate).Select(r => (IReadOnlyList<string>)
                    [
                        r.UnionName, Date(r.MembershipDate), Label<UnionCoverageType>(r.CoverageType), YesNo(r.IsUnionOfficer), Date(r.EndDate),
                    ])));

        return EmployeeCardPdfSectionResult.Combine(personal, union);
    }
}
```

`CustomFieldsCardPdfSection` sınıfını yazmadan önce `CustomFieldValueDto` tanımını oku (`grep -rn "record CustomFieldValueDto" src/Modules/Employee`). Etiket için tanımın görünen ad alanı, değer için metin değeri kullanılır. Değer bir listeyse `string.Join(", ", ...)`, bool ise `YesNo(...)` ile metne çevrilir. Aşağıdaki kod alan adlarının `Label` ve `Value` (string?) olduğu durumu gösterir. Farklıysa yalnızca bu iki erişim değişir.

```csharp
public sealed class CustomFieldsCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.CustomFields;

    // Custom fields are already filtered by EmployeeDetailBuilder (sensitive fields + AccountShowCustomFields setting).
    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken) =>
        EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(await DetailAsync(request, cancellationToken), e =>
            e.CustomFields is { Count: > 0 } fields
                ? [KeyValues(null, fields.Select(f => (f.Label, Text(f.Value))).ToArray())]
                : Array.Empty<SimplePdfBlock>()));
}

public sealed class CareerCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.Career;

    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var careerResult = request.IsOwnCard
            ? await Dispatcher.Query(new GetMyCareerQuery(), cancellationToken)
            : await Dispatcher.Query(new GetEmployeeCareerQuery(request.EmployeeId), cancellationToken);
        var career = EmployeeCardPdfSectionResult.Part(careerResult, rows => Table(
            "Kariyer geçmişi",
            ["Başlangıç", "Bitiş", "Değişiklik", "Çalışma tipi", "Yönetici", "Not"],
            rows.OrderByDescending(r => r.ValidFrom).Select(r => (IReadOnlyList<string>)
            [
                Date(r.ValidFrom), Date(r.ValidTo), Label<CareerChangeType>(r.ChangeType), Label(r.EmploymentType),
                Text(r.ManagerDisplayName), Text(r.Note),
            ])));

        // Employment periods have no self-service query.
        var periods = request.IsOwnCard
            ? null
            : EmployeeCardPdfSectionResult.Part(
                await Dispatcher.Query(new GetEmploymentHistoryQuery(request.EmployeeId), cancellationToken),
                history => Table(
                    "İstihdam dönemleri",
                    ["Başlangıç", "Bitiş", "Çıkış nedeni", "Sayılan gün"],
                    history.Periods.OrderByDescending(p => p.StartDate).Select(p => (IReadOnlyList<string>)
                    [
                        Date(p.StartDate), Date(p.EndDate), Text(p.TerminationReasonName), p.CountedDays.ToString(),
                    ]),
                    [3]));

        return EmployeeCardPdfSectionResult.Combine(career, periods);
    }
}

public sealed class EmployeePaymentsCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.Payments;

    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var salaryResult = request.IsOwnCard
            ? await Dispatcher.Query(new GetMySalariesQuery(), cancellationToken)
            : await Dispatcher.Query(new GetEmployeeSalariesQuery(request.EmployeeId), cancellationToken);
        var salaries = EmployeeCardPdfSectionResult.Part(salaryResult, rows => Table(
            "Maaş geçmişi",
            ["Geçerlilik", "Tutar", "Maaş tipi", "Ödeme sıklığı", "Yol", "Yemek", "Güncel"],
            rows.OrderByDescending(r => r.EffectiveFrom).Select(r => (IReadOnlyList<string>)
            [
                Date(r.EffectiveFrom), Amount(r.Amount, r.Currency), Label<SalaryType>(r.SalaryType), Label<PayFrequency>(r.PayFrequency),
                Amount(r.TransportAllowance), Amount(r.MealAllowance), YesNo(r.IsCurrent),
            ]),
            [1, 4, 5]));

        // IBANs arrive masked without employee.sensitive.view (the own card sees its own IBAN unmasked, as on screen).
        var bankResult = request.IsOwnCard
            ? await Dispatcher.Query(new GetMyBankAccountsQuery(), cancellationToken)
            : await Dispatcher.Query(new GetEmployeeBankAccountsQuery(request.EmployeeId), cancellationToken);
        var banks = EmployeeCardPdfSectionResult.Part(bankResult, rows => Table(
            "Banka hesapları",
            ["IBAN", "Banka", "Hesap tipi", "Para birimi", "Birincil"],
            rows.Select(r => (IReadOnlyList<string>)
            [
                r.Iban, Text(r.BankName), Label<BankAccountType>(r.AccountType), Text(r.Currency), YesNo(r.IsPrimary),
            ])));

        return EmployeeCardPdfSectionResult.Combine(salaries, banks);
    }
}

public sealed class AccountCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer)
    : EmployeeOwnedCardPdfSection(dispatcher, localizer)
{
    public override string Key => EmployeeCardPdfSectionKeys.Account;

    public override async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken) =>
        EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(await DetailAsync(request, cancellationToken), e =>
        [
            KeyValues(
                null,
                ("Hesap durumu", Label<UserAccountStatus>(e.AccountStatus)),
                ("Erişim tipi", Text(e.AccessType)),
                ("Son davet", Timestamp(e.LastInvitationSentAt))),
        ]));
}
```

> `Label<TEnum>(TEnum?)`: nullable olmayan bir enum değeri açık tip argümanıyla (`Label<Gender>(e.Gender)`) verilir. Nullable enum alanları için tip argümanı çıkarılabilir (`Label(e.ContractType)`).

- [ ] **Step 4: DI** (`EmployeeModule.cs`, `EmployeeCardPdfComposer` satırının altına; `using Sense.Modules.Employee.Contracts.CardPdf;` eklenir)

```csharp
        // Employee card PDF sections owned by this module (other modules register theirs in their own AddModule).
        services.AddScoped<IEmployeeCardPdfSection, Infrastructure.CardPdf.GeneralCardPdfSection>();
        services.AddScoped<IEmployeeCardPdfSection, Infrastructure.CardPdf.PersonalCardPdfSection>();
        services.AddScoped<IEmployeeCardPdfSection, Infrastructure.CardPdf.CustomFieldsCardPdfSection>();
        services.AddScoped<IEmployeeCardPdfSection, Infrastructure.CardPdf.CareerCardPdfSection>();
        services.AddScoped<IEmployeeCardPdfSection, Infrastructure.CardPdf.EmployeePaymentsCardPdfSection>();
        services.AddScoped<IEmployeeCardPdfSection, Infrastructure.CardPdf.AccountCardPdfSection>();
```

- [ ] **Step 5: Eksik enum etiketleri.** Şu enum'ların her üyesi için `report.enum.{Enum}.{Member}` satırı `SharedResource.resx` (Türkçe) ve `SharedResource.en.resx` (İngilizce) dosyalarına, mevcut `report.enum.Gender.*` satırlarının yanına eklenir: `MaritalStatus`, `EducationStatus`, `HighestEducationLevel`, `MilitaryStatus`, `DisabilityDegree`, `BloodType`, `CareerChangeType`, `BankAccountType`, `UnionCoverageType`, `UserAccountStatus`.
  - Üyeler enum tanımından okunur.
  - Türkçe/İngilizce metinler, web'deki `public/locales/{tr,en}/employees.json` karşılıklarından alınır.
  - Zaten var olan enum'lar (`Gender`, `ContractType`, `EmploymentType`, `EmployeeStatus`, `SalaryType`, `PayFrequency`) tekrar eklenmez.
  - Kontrol: `grep -c "report.enum.MaritalStatus" src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx` > 0.

- [ ] **Step 6: Testleri ve derlemeyi çalıştır.** Aynı test komutu, ardından `dotnet build src/Modules/Employee/Sense.Modules.Employee.Api`. Expected: 3 PASS, build başarılı.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Employee/Sense.Modules.Employee.Infrastructure/CardPdf src/Modules/Employee/Sense.Modules.Employee.Api/EmployeeModule.cs src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.en.resx tests/Modules/Sense.Modules.Employee.Tests/EmployeeCardPdfSectionsTests.cs
git commit -m "feat(employee): kart PDF'i icin calisan bolumleri"
```

---

### Task 5: Leave sağlayıcıları (leave, overtime)

**Files:**
- Create: `src/Modules/Leave/Sense.Modules.Leave.Infrastructure/CardPdf/LeaveCardPdfSections.cs`
- Modify: `src/Modules/Leave/Sense.Modules.Leave.Api/LeaveModule.cs` (`services.AddLeaveServices(configuration);` altına)
- Modify: `SharedResource{,.en}.resx` (`OvertimeRequestStatus`, `OvertimeDayKind`)
- Test: `tests/Modules/Sense.Modules.Leave.Tests/LeaveCardPdfSectionsTests.cs`

**Interfaces:**
- Consumes:
  - Bakiyeler: `GetEmployeeLeaveBalancesQuery(Guid)`, `GetMyLeaveBalancesQuery()` → `EmployeeLeaveBalancesDto(... IReadOnlyList<LeaveBalanceDto> Balances)`; `LeaveBalanceDto(string LeaveTypeName, LeaveTypeCode, LeaveBalance Balance, decimal PendingDays)`.
  - İzin talepleri: `ListEmployeeLeaveRequestsQuery(Guid, int Take)`, `ListMyLeaveRequestsQuery(int Take)` → `IReadOnlyList<LeaveRequestDto>`.
  - Fazla mesai kayıtları: `ListEmployeeOvertimeRequestsQuery(Guid, int Take)`, `ListMyOvertimeRequestsQuery(int Take)` → `IReadOnlyList<OvertimeRequestDto>`.
  - Yıllık kullanım: `GetEmployeeAnnualOvertimeUsageQuery(Guid, int? Year)`, `GetMyAnnualOvertimeUsageQuery(int? Year)` → `AnnualOvertimeUsageDto(int Year, int UsedMinutes, decimal LimitMinutes, bool Exceeded)`.
  - `PagingDefaults.MaxPageSize` (`Sense.Shared.Contracts.Configuration`).
- Produces: `LeaveCardPdfSection` (Key `leave`, Module `leave`), `OvertimeCardPdfSection` (Key `overtime`, Module `leave`).

> **Sınır:** Leave sorguları tarih filtresi almaz; en fazla `PagingDefaults.MaxPageSize` kayıt döner ve dönem bellekte süzülür. Bu sınır 500'den küçükse PDF en yeni `MaxPageSize` kaydı içerir. Bu durum handoff'a sınırlama olarak yazılır.

- [ ] **Step 1: Failing testleri yaz**

```csharp
using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Infrastructure.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

public sealed class LeaveCardPdfSectionsTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static readonly EmployeeCardPdfRequest Request =
        new(Guid.NewGuid(), EmployeeId, false, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

    private static IStringLocalizer<SharedResource> Localizer()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        return localizer;
    }

    private static LeaveRequestDto LeaveRequest(DateOnly start, DateOnly end) => new(
        Guid.NewGuid(), EmployeeId, Guid.NewGuid(), LeaveTypeCode.Annual, start, end, 1m, null, LeaveRequestStatus.Approved,
        LeaveRequestSource.SelfService, null, 0, DateTime.UtcNow, null, "Yıllık izin");

    private static IDispatcher Dispatcher(IReadOnlyList<LeaveRequestDto> requests, bool balancesForbidden = false)
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeLeaveBalancesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(balancesForbidden
                ? Result.Failure<EmployeeLeaveBalancesDto>(Error.Forbidden("auth.forbidden"))
                : Result.Success(new EmployeeLeaveBalancesDto(EmployeeId, Guid.NewGuid(), new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1), []))));
        dispatcher.Query(Arg.Any<ListEmployeeLeaveRequestsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(requests)));
        return dispatcher;
    }

    [Fact]
    public async Task Leave_FiltersRequestsByPeriod()
    {
        var inside = LeaveRequest(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3));
        var outside = LeaveRequest(new DateOnly(2025, 3, 2), new DateOnly(2025, 3, 3));

        var result = await new LeaveCardPdfSection(Dispatcher([inside, outside]), Localizer()).BuildAsync(Request, CancellationToken.None);

        var table = result.Blocks!.OfType<SimplePdfTable>().Single(t => t.Heading == "İzin talepleri");
        table.Rows.ShouldHaveSingleItem()[1].ShouldBe("02.03.2026");
    }

    [Fact]
    public async Task Leave_BalancesForbidden_StillShowsRequests()
    {
        var dispatcher = Dispatcher([LeaveRequest(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2))], balancesForbidden: true);

        var result = await new LeaveCardPdfSection(dispatcher, Localizer()).BuildAsync(Request, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfTable>().ShouldHaveSingleItem().Heading.ShouldBe("İzin talepleri");
    }

    [Fact]
    public async Task Leave_OwnCard_UsesMyQueries()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetMyLeaveBalancesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<EmployeeLeaveBalancesDto>(Error.Forbidden("auth.forbidden"))));
        dispatcher.Query(Arg.Any<ListMyLeaveRequestsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<LeaveRequestDto>>([])));

        var result = await new LeaveCardPdfSection(dispatcher, Localizer()).BuildAsync(Request with { IsOwnCard = true }, CancellationToken.None);

        result.IsAvailable.ShouldBeTrue();
        await dispatcher.DidNotReceive().Query(Arg.Any<ListEmployeeLeaveRequestsQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Overtime_AllForbidden_IsUnavailable()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<ListEmployeeOvertimeRequestsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<IReadOnlyList<OvertimeRequestDto>>(Error.Forbidden("auth.forbidden"))));
        dispatcher.Query(Arg.Any<GetEmployeeAnnualOvertimeUsageQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<AnnualOvertimeUsageDto>(Error.Forbidden("auth.forbidden"))));

        var result = await new OvertimeCardPdfSection(dispatcher, Localizer()).BuildAsync(Request, CancellationToken.None);

        result.IsAvailable.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `export PATH="/c/Program Files/dotnet:$PATH"; dotnet test tests/Modules/Sense.Modules.Leave.Tests --filter "FullyQualifiedName~LeaveCardPdfSectionsTests"`
Expected: derleme hatası (`LeaveCardPdfSection` yok).

- [ ] **Step 3: Sağlayıcılar** (`LeaveCardPdfSections.cs`)

```csharp
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Leave.Infrastructure.CardPdf;

/// <summary>Card PDF "İzinler": current balances (always current) + leave requests within the period.</summary>
public sealed class LeaveCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Leave;

    public int Order => 0;

    public string? ModuleKey => ProductModuleKeys.Leave;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var balanceResult = request.IsOwnCard
            ? await dispatcher.Query(new GetMyLeaveBalancesQuery(), cancellationToken)
            : await dispatcher.Query(new GetEmployeeLeaveBalancesQuery(request.EmployeeId), cancellationToken);
        var balances = EmployeeCardPdfSectionResult.Part(balanceResult, dto => Table(
            "İzin bakiyeleri",
            ["İzin türü", "Dönem", "Hak edilen", "Kullanılan", "Bekleyen", "Kalan"],
            dto.Balances.Select(b => (IReadOnlyList<string>)
            [
                b.LeaveTypeName,
                $"{Date(b.Balance.CurrentPeriodFrom)} – {Date(b.Balance.CurrentPeriodTo)}",
                Amount(b.Balance.TotalAccrued),
                Amount(b.Balance.TotalUsed),
                Amount(b.PendingDays),
                Amount(b.Balance.AvailableBalance),
            ]),
            [2, 3, 4, 5]));

        var requestResult = request.IsOwnCard
            ? await dispatcher.Query(new ListMyLeaveRequestsQuery(PagingDefaults.MaxPageSize), cancellationToken)
            : await dispatcher.Query(new ListEmployeeLeaveRequestsQuery(request.EmployeeId, PagingDefaults.MaxPageSize), cancellationToken);
        var requests = EmployeeCardPdfSectionResult.Part(requestResult, rows => Table(
            "İzin talepleri",
            ["İzin türü", "Başlangıç", "Bitiş", "Gün", "Durum", "Açıklama"],
            rows.Where(r => InPeriod(r.StartDate, r.EndDate, request.From, request.To))
                .OrderByDescending(r => r.StartDate)
                .Select(r => (IReadOnlyList<string>)
                [
                    TypeName(r), Date(r.StartDate), Date(r.EndDate), Amount(r.CalculatedDays),
                    localizer.EnumText(r.Status) ?? Empty, Text(r.Description),
                ]),
            [3]));

        return EmployeeCardPdfSectionResult.Combine(balances, requests);
    }

    private string TypeName(LeaveRequestDto request) =>
        string.IsNullOrWhiteSpace(request.LeaveTypeName) ? localizer.EnumText(request.LeaveTypeCode) ?? Empty : request.LeaveTypeName;
}

/// <summary>Card PDF "Fazla Mesai": this year's legal-limit usage + overtime requests within the period.</summary>
public sealed class OvertimeCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Overtime;

    public int Order => 0;

    public string? ModuleKey => ProductModuleKeys.Leave;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var usageResult = request.IsOwnCard
            ? await dispatcher.Query(new GetMyAnnualOvertimeUsageQuery(), cancellationToken)
            : await dispatcher.Query(new GetEmployeeAnnualOvertimeUsageQuery(request.EmployeeId), cancellationToken);
        var usage = EmployeeCardPdfSectionResult.Part(usageResult, u =>
        [
            KeyValues(
                $"{u.Year} yılı kullanım",
                ("Kullanılan (saat)", Amount(u.UsedMinutes / 60m)),
                ("Yasal sınır (saat)", Amount(u.LimitMinutes / 60m)),
                ("Sınır aşıldı", YesNo(u.Exceeded))),
        ]);

        var listResult = request.IsOwnCard
            ? await dispatcher.Query(new ListMyOvertimeRequestsQuery(PagingDefaults.MaxPageSize), cancellationToken)
            : await dispatcher.Query(new ListEmployeeOvertimeRequestsQuery(request.EmployeeId, PagingDefaults.MaxPageSize), cancellationToken);
        var list = EmployeeCardPdfSectionResult.Part(listResult, rows => Table(
            "Fazla mesai kayıtları",
            ["Tarih", "Başlangıç", "Süre (saat)", "Gün türü", "Durum", "Açıklama"],
            rows.Where(r => InPeriod(r.WorkDate, null, request.From, request.To))
                .OrderByDescending(r => r.WorkDate)
                .Select(r => (IReadOnlyList<string>)
                [
                    Date(r.WorkDate), r.StartTime.ToString("HH:mm"), Amount(r.DurationMinutes / 60m),
                    localizer.EnumText(r.DayKind) ?? Empty, localizer.EnumText(r.Status) ?? Empty, Text(r.Description),
                ]),
            [2]));

        return EmployeeCardPdfSectionResult.Combine(usage, list);
    }
}
```

- [ ] **Step 4: DI** (`LeaveModule.cs`, `services.AddLeaveServices(configuration);` altına)

```csharp
        // Employee card PDF sections (API only; Employee collects every IEmployeeCardPdfSection).
        services.AddScoped<Sense.Modules.Employee.Contracts.CardPdf.IEmployeeCardPdfSection, Infrastructure.CardPdf.LeaveCardPdfSection>();
        services.AddScoped<Sense.Modules.Employee.Contracts.CardPdf.IEmployeeCardPdfSection, Infrastructure.CardPdf.OvertimeCardPdfSection>();
```

- [ ] **Step 5: Enum etiketleri.** tr/en resx'e eklenir:
  - `report.enum.OvertimeRequestStatus.*`: Pending "Onay bekliyor" / "Pending", Approved "Onaylandı" / "Approved", Rejected "Reddedildi" / "Rejected", Withdrawn "Geri çekildi" / "Withdrawn", Cancelled "İptal edildi" / "Cancelled".
  - `report.enum.OvertimeDayKind.*`: Workday "İş günü" / "Workday", WeeklyRest "Hafta tatili" / "Weekly rest day", PublicHoliday "Resmi tatil" / "Public holiday".

- [ ] **Step 6: Testleri ve derlemeyi çalıştır.** Aynı test komutu ve `dotnet build src/Modules/Leave/Sense.Modules.Leave.Api`. Expected: 4 PASS, build başarılı.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Leave/Sense.Modules.Leave.Infrastructure/CardPdf src/Modules/Leave/Sense.Modules.Leave.Api/LeaveModule.cs src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.en.resx tests/Modules/Sense.Modules.Leave.Tests/LeaveCardPdfSectionsTests.cs
git commit -m "feat(leave): kart PDF'i icin izin ve fazla mesai bolumleri"
```

---

### Task 6: Payroll sağlayıcısı

**Files:**
- Create: `src/Modules/Payroll/Sense.Modules.Payroll.Infrastructure/CardPdf/PayrollCardPdfSection.cs`
- Modify: `src/Modules/Payroll/Sense.Modules.Payroll.Api/PayrollModule.cs` (`services.AddPayrollServices(configuration);` altına)
- Modify: `SharedResource{,.en}.resx` (`PayrollPeriodStatus`, `GarnishmentType`, `WageBasis`, `SgkStatus`, `PayrollPaymentMethod`)
- Test: `tests/Modules/Sense.Modules.Payroll.Tests/PayrollCardPdfSectionTests.cs`

**Interfaces:**
- Consumes:
  - `GetCurrentEmployeePayrollProfileQuery(Guid EmployeeId, DateOnly? AsOf = null)` → `EmployeePayrollProfileDto`. Profil yoksa NotFound döner.
  - `ListEmployeePayslipsQuery(Guid)` → `IReadOnlyList<EmployeePayslipDto(PayslipId, PeriodId, Year, Month, PeriodStatus, GrossAmount, NetAmount, EmployerCost)>`.
  - `ListGarnishmentOrdersQuery(Guid)` → `IReadOnlyList<GarnishmentOrderDto>`.
  - `GetMyPayslipsQuery()` → `IReadOnlyList<MyPayslipDto(PayslipId, Year, Month, GrossAmount, NetAmount, ...)>`.
- Produces: `PayrollCardPdfSection` (Key `payroll`, Module `payroll`).

Dönem kuralı:
- Pusulanın ayı [ayın 1'i, ayın son günü] ile kesişime bakılır.
- Haciz emirleri ve profil durum türüdür, dönemle süzülmez.
- Profil NotFound ise (henüz profil yok) ve pusula/haciz parçalarından en az biri görünüyorsa, profil yerine "Bordro profili: kayıt yok." satırı basılır.

- [ ] **Step 1: Failing testleri yaz**

```csharp
using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Payroll.Application.Garnishments;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Application.Profiles;
using Sense.Modules.Payroll.Infrastructure.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

public sealed class PayrollCardPdfSectionTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly EmployeeCardPdfRequest Request =
        new(Guid.NewGuid(), EmployeeId, false, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

    private static IStringLocalizer<SharedResource> Localizer()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        return localizer;
    }

    private static EmployeePayslipDto Payslip(int year, int month) =>
        new(Guid.NewGuid(), Guid.NewGuid(), year, month, PayrollPeriodStatus.Paid, 50000m, 38000m, 60000m);

    [Fact]
    public async Task Payroll_FiltersPayslipsByPeriod_AndShowsMissingProfileLine()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetCurrentEmployeePayrollProfileQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<EmployeePayrollProfileDto>(Error.NotFound("payroll.profile_not_found"))));
        dispatcher.Query(Arg.Any<ListEmployeePayslipsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<EmployeePayslipDto>>([Payslip(2026, 3), Payslip(2025, 3)])));
        dispatcher.Query(Arg.Any<ListGarnishmentOrdersQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<GarnishmentOrderDto>>([])));

        var result = await new PayrollCardPdfSection(dispatcher, Localizer()).BuildAsync(Request, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfTable>().Single(t => t.Heading == "Bordro pusulaları").Rows.ShouldHaveSingleItem()[0].ShouldBe("2026-03");
        result.Blocks!.OfType<SimplePdfParagraph>().ShouldContain(p => p.Text == "Bordro profili: kayıt yok.");
    }

    [Fact]
    public async Task Payroll_OwnCard_UsesMyPayslipsOnly()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetMyPayslipsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<MyPayslipDto>>([])));

        var result = await new PayrollCardPdfSection(dispatcher, Localizer()).BuildAsync(Request with { IsOwnCard = true }, CancellationToken.None);

        result.IsAvailable.ShouldBeTrue();
        await dispatcher.DidNotReceive().Query(Arg.Any<GetCurrentEmployeePayrollProfileQuery>(), Arg.Any<CancellationToken>());
    }
}
```

`PayrollPeriodStatus` için `using`'i derleyicinin istediği namespace'ten ekle (`grep -rn "enum PayrollPeriodStatus" src/Modules/Payroll`).

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `export PATH="/c/Program Files/dotnet:$PATH"; dotnet test tests/Modules/Sense.Modules.Payroll.Tests --filter "FullyQualifiedName~PayrollCardPdfSectionTests"`
Expected: derleme hatası.

- [ ] **Step 3: Sağlayıcı**

```csharp
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Payroll.Application.Garnishments;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Application.Profiles;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using Sense.Shared.Kernel.Results;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Payroll.Infrastructure.CardPdf;

/// <summary>Card PDF "Bordro": current payroll profile, payslips within the period, garnishment orders.</summary>
public sealed class PayrollCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    private const string PayslipsHeading = "Bordro pusulaları";

    public string Key => EmployeeCardPdfSectionKeys.Payroll;

    public int Order => 0;

    public string? ModuleKey => ProductModuleKeys.Payroll;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        if (request.IsOwnCard)
        {
            // Self-service has payslips only (no profile/garnishment query for the employee).
            var mine = EmployeeCardPdfSectionResult.Part(
                await dispatcher.Query(new GetMyPayslipsQuery(), cancellationToken),
                rows => Table(
                    PayslipsHeading,
                    ["Dönem", "Brüt", "Net"],
                    rows.Where(r => InMonth(r.Year, r.Month, request))
                        .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
                        .Select(r => (IReadOnlyList<string>)[Month(r.Year, r.Month), Amount(r.GrossAmount), Amount(r.NetAmount)]),
                    [1, 2]));
            return EmployeeCardPdfSectionResult.Combine(mine);
        }

        var profileResult = await dispatcher.Query(new GetCurrentEmployeePayrollProfileQuery(request.EmployeeId), cancellationToken);
        var profile = EmployeeCardPdfSectionResult.Part(profileResult, p =>
        [
            KeyValues(
                "Bordro profili",
                ("Geçerlilik", $"{Date(p.ValidFrom)} – {Date(p.ValidTo)}"),
                ("Ücret esası", localizer.EnumText(p.WageBasis) ?? Empty),
                ("SGK durumu", localizer.EnumText(p.SgkStatus) ?? Empty),
                ("Teşvik kanunu", Text(p.IncentiveLawCode)),
                ("Yemek günlük", Amount(p.MealDailyAmount)),
                ("Yol günlük", Amount(p.TransportDailyAmount)),
                ("Ödeme yöntemi", localizer.EnumText(p.PaymentMethod) ?? Empty),
                ("Hastalıkta kesinti", YesNo(p.SickDaysReducePay)),
                ("Dış bordro kodu", Text(p.ExternalPayrollCode))),
        ]);

        var payslips = EmployeeCardPdfSectionResult.Part(
            await dispatcher.Query(new ListEmployeePayslipsQuery(request.EmployeeId), cancellationToken),
            rows => Table(
                PayslipsHeading,
                ["Dönem", "Brüt", "Net", "İşveren maliyeti", "Durum"],
                rows.Where(r => InMonth(r.Year, r.Month, request))
                    .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
                    .Select(r => (IReadOnlyList<string>)
                    [
                        Month(r.Year, r.Month), Amount(r.GrossAmount), Amount(r.NetAmount), Amount(r.EmployerCost),
                        localizer.EnumText(r.PeriodStatus) ?? Empty,
                    ]),
                [1, 2, 3]));

        var garnishments = EmployeeCardPdfSectionResult.Part(
            await dispatcher.Query(new ListGarnishmentOrdersQuery(request.EmployeeId), cancellationToken),
            rows => Table(
                "İcra / nafaka emirleri",
                ["Tür", "Dosya no", "Aylık taksit", "Kalan", "Başlangıç", "Kapanış"],
                rows.OrderByDescending(r => r.StartDate).Select(r => (IReadOnlyList<string>)
                [
                    localizer.EnumText(r.Type) ?? Empty, r.CaseReference, Amount(r.MonthlyInstallmentAmount),
                    Amount(r.RemainingBalance), Date(r.StartDate), Date(r.ClosedDate),
                ]),
                [2, 3]));

        // No profile yet (NotFound) is a normal state for a visible payroll tab: say so instead of dropping the block.
        var profileMissing = profileResult.IsFailure && profileResult.Error.Type == ErrorType.NotFound;
        if (profile is null && profileMissing && (payslips is not null || garnishments is not null))
        {
            profile = [new SimplePdfParagraph("Bordro profili: kayıt yok.")];
        }

        return EmployeeCardPdfSectionResult.Combine(profile, payslips, garnishments);
    }

    private static bool InMonth(int year, int month, EmployeeCardPdfRequest request)
    {
        var start = new DateOnly(year, month, 1);
        return InPeriod(start, start.AddMonths(1).AddDays(-1), request.From, request.To);
    }

    private static string Month(int year, int month) => $"{year:D4}-{month:D2}";
}
```

- [ ] **Step 4: DI** (`PayrollModule.cs`, `services.AddPayrollServices(configuration);` altına)

```csharp
        // Employee card PDF section (API only).
        services.AddScoped<Sense.Modules.Employee.Contracts.CardPdf.IEmployeeCardPdfSection, Infrastructure.CardPdf.PayrollCardPdfSection>();
```

- [ ] **Step 5: Enum etiketleri.** Şu etiketler tr/en resx'e eklenir:
  - `report.enum.PayrollPeriodStatus.*`: Open "Açık", Approved "Onaylandı", Paid "Ödendi", Locked "Kilitli".
  - `report.enum.GarnishmentType.*`: Icra "İcra", Nafaka "Nafaka".
  - `WageBasis`, `SgkStatus`, `PayrollPaymentMethod`: bu enum'ların tüm üyeleri. Üyeler `src/Modules/Payroll/Sense.Modules.Payroll.Domain/PayrollEnums.cs`'ten okunur, metinler `web/public/locales/{tr,en}/payroll.json` karşılıklarından alınır.

- [ ] **Step 6: Testleri ve derlemeyi çalıştır.** Aynı test komutu ve `dotnet build src/Modules/Payroll/Sense.Modules.Payroll.Api`. Expected: 2 PASS, build başarılı.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Payroll/Sense.Modules.Payroll.Infrastructure/CardPdf src/Modules/Payroll/Sense.Modules.Payroll.Api/PayrollModule.cs src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.en.resx tests/Modules/Sense.Modules.Payroll.Tests/PayrollCardPdfSectionTests.cs
git commit -m "feat(payroll): kart PDF'i icin bordro bolumu"
```

---

### Task 7: Assets ve Documents sağlayıcıları

**Files:**
- Create: `src/Modules/Assets/Sense.Modules.Assets.Infrastructure/CardPdf/AssetsCardPdfSection.cs`
- Create: `src/Modules/Documents/Sense.Modules.Documents.Infrastructure/CardPdf/DocumentsCardPdfSection.cs`
- Modify: `src/Modules/Assets/Sense.Modules.Assets.Api/AssetsModule.cs` (`services.AddAssetsCoreServices();` altına)
- Modify: `src/Modules/Documents/Sense.Modules.Documents.Api/DocumentsModule.cs` (`services.AddDocumentsCoreServices();` altına)
- Modify: `SharedResource{,.en}.resx` (`AssetStatus`)
- Test: `tests/Modules/Sense.Modules.Assets.Tests/AssetsCardPdfSectionTests.cs`, `tests/Modules/Sense.Modules.Documents.Tests/DocumentsCardPdfSectionTests.cs`

**Interfaces:**
- Consumes:
  - `GetEmployeeAssetsQuery(Guid)`, `GetMyAssetsQuery()` → `IReadOnlyList<AssetDto(Id, CategoryId, Name, SerialNumber, Description, AssetStatus Status, AssignedEmployeeId, DateOnly? IssuedAt, DateOnly? ReturnedAt, DateOnly? ReplacementDueOn)>`.
  - `GetEmployeeDocumentRequirementsQuery(Guid)`, `GetMyDocumentRequirementsQuery()` → `IReadOnlyList<EmployeeDocumentRequirementDto(Id, EmployeeId, DocumentTypeId, DocumentTypeCode, DocumentTypeName, DocumentTypeStage Stage, DocumentRequirementStatus Status, DateOnly? DueDate, Guid? DocumentId, string? WaivedReason, DateOnly? ExpiryDate)>`.
- Produces:
  - `AssetsCardPdfSection` (Key `assets`, Module `assets`).
  - `DocumentsCardPdfSection` (Key `documents`, Module `null`, çünkü Documents çekirdek modüldür).

Zimmet dönem kuralı:
- `IssuedAt` null ise kayıt her zaman listelenir (atama bekliyor).
- Diğer kayıtlar için bitiş `ReturnedAt ?? DateOnly.MaxValue` alınır. Yani hâlâ zimmetteki bir varlık, dönemden önce teslim edilmiş olsa bile listelenir.

- [ ] **Step 1: Failing testleri yaz**

`AssetsCardPdfSectionTests.cs`:
```csharp
using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Modules.Assets.Infrastructure.CardPdf;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Assets.Tests;

public sealed class AssetsCardPdfSectionTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static IStringLocalizer<SharedResource> Localizer()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        return localizer;
    }

    private static AssetDto Asset(string name, DateOnly? issued, DateOnly? returned) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, "SN-1", null, AssetStatus.Assigned, EmployeeId, issued, returned, null);

    [Fact]
    public async Task Assets_FiltersByPeriod_KeepsOpenAndPendingAssignments()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeAssetsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<AssetDto>>(
            [
                Asset("Laptop", new DateOnly(2026, 2, 1), null),
                Asset("Monitör", new DateOnly(2024, 1, 1), null),
                Asset("Telefon", new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 1)),
                Asset("Kart", null, null),
            ])));
        var request = new EmployeeCardPdfRequest(Guid.NewGuid(), EmployeeId, false, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        var result = await new AssetsCardPdfSection(dispatcher, Localizer()).BuildAsync(request, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfTable>().Single().Rows.Select(r => r[0])
            .ShouldBe(["Laptop", "Monitör", "Kart"], ignoreOrder: true);
    }

    [Fact]
    public async Task Assets_Forbidden_IsUnavailable()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeeAssetsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<IReadOnlyList<AssetDto>>(Error.Forbidden("auth.forbidden"))));

        var result = await new AssetsCardPdfSection(dispatcher, Localizer())
            .BuildAsync(new EmployeeCardPdfRequest(Guid.NewGuid(), EmployeeId, false, null, null), CancellationToken.None);

        result.IsAvailable.ShouldBeFalse();
    }
}
```

`DocumentsCardPdfSectionTests.cs`:
```csharp
using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Documents.Infrastructure.CardPdf;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class DocumentsCardPdfSectionTests
{
    [Fact]
    public async Task Documents_OwnCard_UsesMyQueryAndListsRequirements()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        var dispatcher = Substitute.For<IDispatcher>();
        var employeeId = Guid.NewGuid();
        dispatcher.Query(Arg.Any<GetMyDocumentRequirementsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<EmployeeDocumentRequirementDto>>(
            [
                new(Guid.NewGuid(), employeeId, Guid.NewGuid(), "HEALTH", "Sağlık raporu", DocumentTypeStage.Onboarding,
                    DocumentRequirementStatus.Missing, new DateOnly(2026, 10, 1), null, null),
            ])));

        var result = await new DocumentsCardPdfSection(dispatcher, localizer)
            .BuildAsync(new EmployeeCardPdfRequest(Guid.NewGuid(), employeeId, true, null, null), CancellationToken.None);

        result.Blocks!.OfType<SimplePdfTable>().Single().Rows.ShouldHaveSingleItem()[0].ShouldBe("Sağlık raporu");
        await dispatcher.DidNotReceive().Query(Arg.Any<GetEmployeeDocumentRequirementsQuery>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

Run: `export PATH="/c/Program Files/dotnet:$PATH"; dotnet test tests/Modules/Sense.Modules.Assets.Tests --filter "FullyQualifiedName~CardPdf"; dotnet test tests/Modules/Sense.Modules.Documents.Tests --filter "FullyQualifiedName~CardPdf"`
Expected: iki projede de derleme hatası.

- [ ] **Step 3: Sağlayıcılar**

`AssetsCardPdfSection.cs`:
```csharp
using Microsoft.Extensions.Localization;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Assets.Infrastructure.CardPdf;

/// <summary>Card PDF "Zimmetler": assets held during the period; still-held and pending assignments always listed.</summary>
public sealed class AssetsCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Assets;

    public int Order => 0;

    public string? ModuleKey => ProductModuleKeys.Assets;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var result = request.IsOwnCard
            ? await dispatcher.Query(new GetMyAssetsQuery(), cancellationToken)
            : await dispatcher.Query(new GetEmployeeAssetsQuery(request.EmployeeId), cancellationToken);
        return EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(result, rows => Table(
            "Zimmetler",
            ["Varlık", "Seri no", "Durum", "Teslim", "İade", "Yenileme"],
            rows.Where(a => a.IssuedAt is not { } issued
                    || InPeriod(issued, a.ReturnedAt ?? DateOnly.MaxValue, request.From, request.To))
                .OrderByDescending(a => a.IssuedAt ?? DateOnly.MaxValue)
                .Select(a => (IReadOnlyList<string>)
                [
                    a.Name, Text(a.SerialNumber), localizer.EnumText(a.Status) ?? Empty,
                    Date(a.IssuedAt), Date(a.ReturnedAt), Date(a.ReplacementDueOn),
                ]))));
    }
}
```

`DocumentsCardPdfSection.cs`:
```csharp
using Microsoft.Extensions.Localization;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Documents.Infrastructure.CardPdf;

/// <summary>Card PDF "Evraklar": requirement list with status (state data, not period-filtered; no file contents).</summary>
public sealed class DocumentsCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Documents;

    public int Order => 0;

    public string? ModuleKey => null;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var result = request.IsOwnCard
            ? await dispatcher.Query(new GetMyDocumentRequirementsQuery(), cancellationToken)
            : await dispatcher.Query(new GetEmployeeDocumentRequirementsQuery(request.EmployeeId), cancellationToken);
        return EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(result, rows => Table(
            "Evrak gereksinimleri",
            ["Evrak", "Aşama", "Durum", "Son tarih", "Geçerlilik sonu", "Muafiyet nedeni"],
            rows.OrderBy(r => r.Stage).ThenBy(r => r.DocumentTypeName).Select(r => (IReadOnlyList<string>)
            [
                r.DocumentTypeName, localizer.EnumText(r.Stage) ?? Empty, localizer.EnumText(r.Status) ?? Empty,
                Date(r.DueDate), Date(r.ExpiryDate), Text(r.WaivedReason),
            ]))));
    }
}
```

- [ ] **Step 4: DI**

`AssetsModule.cs`, `services.AddAssetsCoreServices();` altına:
```csharp
        // Employee card PDF section (API only).
        services.AddScoped<Sense.Modules.Employee.Contracts.CardPdf.IEmployeeCardPdfSection, Infrastructure.CardPdf.AssetsCardPdfSection>();
```

`DocumentsModule.cs`, `services.AddDocumentsCoreServices();` altına:
```csharp
        // Employee card PDF section (API only).
        services.AddScoped<Sense.Modules.Employee.Contracts.CardPdf.IEmployeeCardPdfSection, Infrastructure.CardPdf.DocumentsCardPdfSection>();
```

Infrastructure projeleri `Employee.Contracts`'ı Application üzerinden geçişli alır. Derleme tipi bulamazsa ilgili `*.Infrastructure.csproj`'a şu satır eklenir: `<ProjectReference Include="../../Employee/Sense.Modules.Employee.Contracts/Sense.Modules.Employee.Contracts.csproj" />`.

- [ ] **Step 5: Enum etiketleri.** `report.enum.AssetStatus.*` tr/en resx'e eklenir: Unassigned "Boşta" / "Unassigned", Assigned "Zimmetli" / "Assigned", Retired "Hizmet dışı" / "Retired", PendingAssignment "Atama bekliyor" / "Pending assignment".

- [ ] **Step 6: Testleri ve derlemeleri çalıştır.** Step 2 komutları, ardından `dotnet build src/Modules/Assets/Sense.Modules.Assets.Api` ve `dotnet build src/Modules/Documents/Sense.Modules.Documents.Api`. Expected: 3 PASS, build başarılı.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Assets/Sense.Modules.Assets.Infrastructure/CardPdf src/Modules/Assets/Sense.Modules.Assets.Api/AssetsModule.cs src/Modules/Documents/Sense.Modules.Documents.Infrastructure/CardPdf src/Modules/Documents/Sense.Modules.Documents.Api/DocumentsModule.cs src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.en.resx tests/Modules/Sense.Modules.Assets.Tests/AssetsCardPdfSectionTests.cs tests/Modules/Sense.Modules.Documents.Tests/DocumentsCardPdfSectionTests.cs
git commit -m "feat(assets,documents): kart PDF'i icin zimmet ve evrak bolumleri"
```

(csproj değiştiyse `git add`'e eklenir.)

---

### Task 8: Expense sağlayıcısı (payments, Order 10)

**Files:**
- Create: `src/Modules/Expense/Sense.Modules.Expense.Infrastructure/CardPdf/ExpenseCardPdfSection.cs`
- Modify: `src/Modules/Expense/Sense.Modules.Expense.Infrastructure/Sense.Modules.Expense.Infrastructure.csproj` (Employee.Contracts referansı)
- Modify: `src/Modules/Expense/Sense.Modules.Expense.Api/ExpenseModule.cs` (`services.AddExpenseCoreServices();` altına)
- Modify: `SharedResource{,.en}.resx` (`PaymentKind`)
- Test: `tests/Modules/Sense.Modules.Expense.Tests/ExpenseCardPdfSectionTests.cs`

**Interfaces:**
- Consumes: `GetEmployeePaymentsOverviewQuery(Guid)`, `GetMyPaymentsOverviewQuery()` → `IReadOnlyList<PaymentOverviewRow(PaymentKind Kind, DateOnly Date, decimal Amount, string Currency, string Status, Guid SourceId, string? Description)>`.
- Produces: `ExpenseCardPdfSection` (Key `payments`, Order 10, Module `expense`).

`Status` sunucudan ham enum adı olarak gelir (örneğin `Approved`) ve olduğu gibi basılır. Bu bilinen bir sınırlamadır ve handoff'a yazılır.

- [ ] **Step 1: Failing test**

```csharp
using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Expense.Application.Payments;
using Sense.Modules.Expense.Infrastructure.CardPdf;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Expense.Tests;

public sealed class ExpenseCardPdfSectionTests
{
    [Fact]
    public async Task Expense_FiltersPaymentsByPeriod()
    {
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Query(Arg.Any<GetEmployeePaymentsOverviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<PaymentOverviewRow>>(
            [
                new(PaymentKind.Expense, new DateOnly(2026, 4, 1), 100m, "TRY", "Approved", Guid.NewGuid(), "Taksi"),
                new(PaymentKind.Advance, new DateOnly(2024, 4, 1), 900m, "TRY", "Paid", Guid.NewGuid(), null),
            ])));
        var request = new EmployeeCardPdfRequest(Guid.NewGuid(), Guid.NewGuid(), false, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        var result = await new ExpenseCardPdfSection(dispatcher, localizer).BuildAsync(request, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfTable>().Single().Rows.ShouldHaveSingleItem()[4].ShouldBe("Taksi");
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `export PATH="/c/Program Files/dotnet:$PATH"; dotnet test tests/Modules/Sense.Modules.Expense.Tests --filter "FullyQualifiedName~ExpenseCardPdfSectionTests"`
Expected: derleme hatası.

- [ ] **Step 3: csproj referansı.** `Sense.Modules.Expense.Infrastructure.csproj` içindeki ProjectReference ItemGroup'unun sonuna eklenir:

```xml
    <ProjectReference Include="../../Employee/Sense.Modules.Employee.Contracts/Sense.Modules.Employee.Contracts.csproj" />
```

- [ ] **Step 4: Sağlayıcı**

```csharp
using Microsoft.Extensions.Localization;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Expense.Application.Payments;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Infrastructure;
using Sense.Shared.Infrastructure.Export;
using static Sense.Modules.Employee.Contracts.CardPdf.EmployeeCardPdfFormat;

namespace Sense.Modules.Expense.Infrastructure.CardPdf;

/// <summary>Card PDF "Ödemeler" (after Employee's salary/bank blocks): expense/advance payments within the period.</summary>
public sealed class ExpenseCardPdfSection(IDispatcher dispatcher, IStringLocalizer<SharedResource> localizer) : IEmployeeCardPdfSection
{
    public string Key => EmployeeCardPdfSectionKeys.Payments;

    public int Order => 10;

    public string? ModuleKey => ProductModuleKeys.Expense;

    public async Task<EmployeeCardPdfSectionResult> BuildAsync(EmployeeCardPdfRequest request, CancellationToken cancellationToken)
    {
        var result = request.IsOwnCard
            ? await dispatcher.Query(new GetMyPaymentsOverviewQuery(), cancellationToken)
            : await dispatcher.Query(new GetEmployeePaymentsOverviewQuery(request.EmployeeId), cancellationToken);
        return EmployeeCardPdfSectionResult.Combine(EmployeeCardPdfSectionResult.Part(result, rows => Table(
            "Masraf ve avans ödemeleri",
            ["Tarih", "Tür", "Tutar", "Durum", "Açıklama"],
            rows.Where(r => InPeriod(r.Date, null, request.From, request.To))
                .OrderByDescending(r => r.Date)
                .Select(r => (IReadOnlyList<string>)
                [
                    Date(r.Date), localizer.EnumText(r.Kind) ?? Empty, Amount(r.Amount, r.Currency), r.Status, Text(r.Description),
                ]),
            [2])));
    }
}
```

- [ ] **Step 5: DI** (`ExpenseModule.cs`, `services.AddExpenseCoreServices();` altına)

```csharp
        // Employee card PDF section (API only).
        services.AddScoped<Sense.Modules.Employee.Contracts.CardPdf.IEmployeeCardPdfSection, Infrastructure.CardPdf.ExpenseCardPdfSection>();
```

- [ ] **Step 6: Enum etiketleri.** `report.enum.PaymentKind.*` tr/en resx'e eklenir: Expense "Masraf" / "Expense", Advance "Avans" / "Advance", Overtime "Fazla mesai" / "Overtime", Adjustment "Düzeltme" / "Adjustment".

- [ ] **Step 7: Test, derleme ve architecture testi**

Run:
- Step 2 komutu
- `dotnet build src/Modules/Expense/Sense.Modules.Expense.Api`
- `dotnet test tests/Sense.Tests.Architecture`

Expected: hepsi yeşil.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Expense/Sense.Modules.Expense.Infrastructure/CardPdf src/Modules/Expense/Sense.Modules.Expense.Infrastructure/Sense.Modules.Expense.Infrastructure.csproj src/Modules/Expense/Sense.Modules.Expense.Api/ExpenseModule.cs src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.en.resx tests/Modules/Sense.Modules.Expense.Tests/ExpenseCardPdfSectionTests.cs
git commit -m "feat(expense): kart PDF'i icin odeme bolumu"
```

---

### Task 9: Web — servis, diyalog, buton, çeviriler

**Files:**
- Modify: `web/src/services/employees.service.ts` (`downloadWorkCertificate` altına)
- Create: `web/src/components/employees/employee-card-pdf-dialog.tsx`
- Create: `web/src/components/employees/employee-card-pdf-dialog.test.tsx`
- Modify: `web/src/pages/employees/employee-card.tsx`
- Modify: `web/public/locales/tr/employees.json`, `en/employees.json`, `de/employees.json` (`card` objesine `pdf` alt objesi)

**Interfaces:**
- Consumes:
  - Task 3 endpoint'leri
  - `useVisibleEmployeeCardTabs()` (`@/components/employees/card-tabs`)
  - `saveDownloadedFile({ blob, fileName })` (`@/services/data-transfer.service`)
  - `DatePicker({ value, onChange, min, max })` (`@/components/ui/date-picker`; değerler `"yyyy-MM-dd"` ya da `""`)
  - `parseContentDispositionFileName` (employees.service.ts'de zaten import'lu)
- Produces:
  - `downloadEmployeeCardPdf(target: string, options: EmployeeCardPdfOptions): Promise<{ blob: Blob; fileName: string }>`
  - `cardPdfPeriod(preset, today, custom): { from?: string; to?: string } | null`
  - `EmployeeCardPdfDialog({ employeeId, isOwnCard, onClose })` (her zaman açık render edilir; ebeveyn yalnız açıkken mount eder)

- [ ] **Step 1: Failing testleri yaz** (`employee-card-pdf-dialog.test.tsx`)

```tsx
import { describe, expect, it } from "vitest";
import { cardPdfPeriod } from "./employee-card-pdf-dialog";

const today = new Date(2026, 8, 28); // 28.09.2026 (months are 0-based)

describe("cardPdfPeriod", () => {
  it("last12Months spans one year back to today", () => {
    expect(cardPdfPeriod("last12Months", today, { from: "", to: "" })).toEqual({ from: "2025-09-28", to: "2026-09-28" });
  });

  it("thisYear starts on 1 January", () => {
    expect(cardPdfPeriod("thisYear", today, { from: "", to: "" })).toEqual({ from: "2026-01-01", to: "2026-09-28" });
  });

  it("allTime sends no bounds", () => {
    expect(cardPdfPeriod("allTime", today, { from: "", to: "" })).toEqual({});
  });

  it("custom with from after to is invalid", () => {
    expect(cardPdfPeriod("custom", today, { from: "2026-05-01", to: "2026-01-01" })).toBeNull();
  });

  it("custom with an empty bound is invalid", () => {
    expect(cardPdfPeriod("custom", today, { from: "2026-01-01", to: "" })).toBeNull();
  });

  it("custom passes both bounds through", () => {
    expect(cardPdfPeriod("custom", today, { from: "2026-01-01", to: "2026-03-31" })).toEqual({
      from: "2026-01-01",
      to: "2026-03-31",
    });
  });
});
```

Bunun yanına bir render testi eklenir. `create-employee-dialog.test.tsx`'teki sarmalayıcı (MantineProvider, i18n, QueryClient) ve auth store kurulumu aynen kullanılır. Test iki şeyi doğrular:
- Diyalogda `employees:card.tabs.other` etiketli checkbox yoktur.
- "Temizle"ye basınca "PDF oluştur" butonu `disabled` olur.

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `cd web && pnpm exec vitest run src/components/employees/employee-card-pdf-dialog.test.tsx`
Expected: FAIL (modül bulunamadı).

- [ ] **Step 3: Servis fonksiyonu** (`employees.service.ts`, `downloadWorkCertificate` altına)

```ts
export interface EmployeeCardPdfOptions {
  sections: string[];
  from?: string;
  to?: string;
}

/** Employee card PDF (selected tabs + optional period); `target` is an employee id or "me" for the own card. */
export async function downloadEmployeeCardPdf(
  target: string,
  options: EmployeeCardPdfOptions
): Promise<{ blob: Blob; fileName: string }> {
  const response = await apiClient.get<Blob>(`${EMPLOYEES_BASE}/${target}/card-pdf`, {
    params: { sections: options.sections.join(","), from: options.from, to: options.to },
    responseType: "blob",
  });
  const disposition = response.headers["content-disposition"];
  return {
    blob: response.data,
    fileName:
      parseContentDispositionFileName(typeof disposition === "string" ? disposition : undefined) ?? "calisan-karti.pdf",
  };
}
```

- [ ] **Step 4: Diyalog** (`employee-card-pdf-dialog.tsx`)

```tsx
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Button, Checkbox, Group, Input, Modal, SegmentedControl, SimpleGrid, Stack, Text } from "@mantine/core";
import { DatePicker } from "@/components/ui/date-picker";
import { useVisibleEmployeeCardTabs } from "@/components/employees/card-tabs";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { saveDownloadedFile } from "@/services/data-transfer.service";
import { downloadEmployeeCardPdf } from "@/services/employees.service";

export type CardPdfPeriodPreset = "last12Months" | "thisYear" | "allTime" | "custom";

const PRESETS: CardPdfPeriodPreset[] = ["last12Months", "thisYear", "allTime", "custom"];
/** Tabs with no printable content of their own (shortcuts only). */
const EXCLUDED_TABS = new Set(["other"]);

function toDateKey(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${month}-${day}`;
}

/** Period sent to the API for a preset; `null` when a custom range is incomplete or reversed. */
export function cardPdfPeriod(
  preset: CardPdfPeriodPreset,
  today: Date,
  custom: { from: string; to: string }
): { from?: string; to?: string } | null {
  const to = toDateKey(today);
  if (preset === "allTime") return {};
  if (preset === "thisYear") return { from: `${today.getFullYear()}-01-01`, to };
  if (preset === "last12Months") {
    const from = new Date(today);
    from.setFullYear(from.getFullYear() - 1);
    return { from: toDateKey(from), to };
  }
  if (!custom.from || !custom.to || custom.from > custom.to) return null;
  return { from: custom.from, to: custom.to };
}

interface EmployeeCardPdfDialogProps {
  employeeId: string;
  isOwnCard: boolean;
  onClose: () => void;
}

export function EmployeeCardPdfDialog({ employeeId, isOwnCard, onClose }: EmployeeCardPdfDialogProps) {
  const { t } = useTranslation(["employees", "common"]);
  const tabs = useVisibleEmployeeCardTabs().filter((tab) => !EXCLUDED_TABS.has(tab.key));
  const [selected, setSelected] = useState<string[]>(() => tabs.map((tab) => tab.key));
  const [preset, setPreset] = useState<CardPdfPeriodPreset>("last12Months");
  const [custom, setCustom] = useState({ from: "", to: "" });
  const period = cardPdfPeriod(preset, new Date(), custom);
  const allSelected = selected.length === tabs.length;
  const customInvalid = preset === "custom" && period === null && Boolean(custom.from && custom.to);

  const mutation = useMutation({
    mutationFn: () =>
      downloadEmployeeCardPdf(isOwnCard ? "me" : employeeId, {
        // Card order, not click order.
        sections: tabs.map((tab) => tab.key).filter((key) => selected.includes(key)),
        ...period,
      }),
    onSuccess: (file) => {
      saveDownloadedFile(file);
      onClose();
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("employees:card.pdf.failed")),
      }),
  });

  return (
    <Modal opened onClose={onClose} title={t("employees:card.pdf.title")} size="lg">
      <Stack gap="md">
        <Group justify="space-between">
          <Text size="sm" fw={500}>
            {t("employees:card.pdf.sections")}
          </Text>
          <Button variant="subtle" size="xs" onClick={() => setSelected(allSelected ? [] : tabs.map((tab) => tab.key))}>
            {allSelected ? t("employees:card.pdf.clearAll") : t("employees:card.pdf.selectAll")}
          </Button>
        </Group>
        <Checkbox.Group value={selected} onChange={setSelected}>
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
            {tabs.map((tab) => (
              <Checkbox key={tab.key} value={tab.key} label={t(tab.labelKey)} />
            ))}
          </SimpleGrid>
        </Checkbox.Group>

        <Input.Wrapper label={t("employees:card.pdf.period")} description={t("employees:card.pdf.periodHint")}>
          <SegmentedControl
            fullWidth
            mt={4}
            value={preset}
            onChange={(value) => setPreset(value as CardPdfPeriodPreset)}
            data={PRESETS.map((value) => ({ value, label: t(`employees:card.pdf.presets.${value}`) }))}
          />
        </Input.Wrapper>
        {preset === "custom" && (
          <SimpleGrid cols={2} spacing="xs">
            <Input.Wrapper label={t("employees:card.pdf.from")}>
              <DatePicker
                value={custom.from}
                max={custom.to || undefined}
                onChange={(from) => setCustom((current) => ({ ...current, from }))}
              />
            </Input.Wrapper>
            <Input.Wrapper label={t("employees:card.pdf.to")} error={customInvalid ? t("employees:card.pdf.invalidPeriod") : undefined}>
              <DatePicker
                value={custom.to}
                min={custom.from || undefined}
                invalid={customInvalid}
                onChange={(to) => setCustom((current) => ({ ...current, to }))}
              />
            </Input.Wrapper>
          </SimpleGrid>
        )}

        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>
            {t("common:cancel")}
          </Button>
          <Button onClick={() => mutation.mutate()} loading={mutation.isPending} disabled={selected.length === 0 || period === null}>
            {t("employees:card.pdf.generate")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
```

`common:cancel` anahtarını doğrula: `grep -n "\"cancel\"" web/public/locales/tr/common.json`. Yoksa, repo'da iptal butonlarının kullandığı mevcut anahtar kullanılır.

- [ ] **Step 5: Kart butonu** (`employee-card.tsx`)

Import'lar:
- `lucide-react` import satırına `FileDown` ekle.
- `import { EmployeeCardPdfDialog } from "@/components/employees/employee-card-pdf-dialog";`

Değişiklikler:
- `CardHeaderBlock` imzasını `function CardHeaderBlock({ employee, isOwnCard }: { employee: EmployeeDetail; isOwnCard: boolean })` yap.
- Çağrıyı `<CardHeaderBlock key={`header-${employee.id}`} employee={employee} isOwnCard={isOwnCard} />` yap.
- `CardHeaderBlock` içinde state'lerin yanına `const [pdfOpen, setPdfOpen] = useState(false);` ekle.
- `{(showInvite || lifecycleActions.length > 0) && (` satırından hemen önce:

```tsx
        <Tooltip label={t("employees:card.pdf.button")}>
          <ActionIcon
            type="button"
            variant="subtle"
            color="gray"
            size="lg"
            aria-label={t("employees:card.pdf.button")}
            onClick={() => setPdfOpen(true)}
          >
            <FileDown size={20} />
          </ActionIcon>
        </Tooltip>
```

- `<EmployeeLifecycleDialogs ... />` satırının altına. Seçim her açılışta sıfırlansın diye diyalog yalnız açıkken mount edilir:

```tsx
      {pdfOpen && <EmployeeCardPdfDialog employeeId={employee.id} isOwnCard={isOwnCard} onClose={() => setPdfOpen(false)} />}
```

- [ ] **Step 6: Çeviriler.** Üç locale dosyasında `"card": { ... }` objesinin içine, `backToList` satırının altına eklenir.

`tr/employees.json`:
```json
    "pdf": {
      "button": "PDF olarak indir",
      "title": "Çalışan kartını PDF olarak indir",
      "sections": "Bölümler",
      "selectAll": "Tümünü seç",
      "clearAll": "Temizle",
      "period": "Dönem",
      "periodHint": "İzin, fazla mesai, pusula, zimmet ve ödeme listeleri bu döneme göre süzülür; diğer bilgiler güncel haliyle gelir.",
      "presets": { "last12Months": "Son 12 ay", "thisYear": "Bu yıl", "allTime": "Tüm zamanlar", "custom": "Özel" },
      "from": "Başlangıç",
      "to": "Bitiş",
      "invalidPeriod": "Bitiş başlangıçtan önce olamaz.",
      "generate": "PDF oluştur",
      "failed": "PDF oluşturulamadı."
    },
```

`en/employees.json`:
```json
    "pdf": {
      "button": "Download as PDF",
      "title": "Download employee card as PDF",
      "sections": "Sections",
      "selectAll": "Select all",
      "clearAll": "Clear",
      "period": "Period",
      "periodHint": "Leave, overtime, payslip, asset and payment lists are filtered by this period; other data is shown as of today.",
      "presets": { "last12Months": "Last 12 months", "thisYear": "This year", "allTime": "All time", "custom": "Custom" },
      "from": "From",
      "to": "To",
      "invalidPeriod": "The end cannot be before the start.",
      "generate": "Create PDF",
      "failed": "The PDF could not be created."
    },
```

`de/employees.json`:
```json
    "pdf": {
      "button": "Als PDF herunterladen",
      "title": "Mitarbeiterakte als PDF herunterladen",
      "sections": "Bereiche",
      "selectAll": "Alle auswählen",
      "clearAll": "Leeren",
      "period": "Zeitraum",
      "periodHint": "Urlaubs-, Überstunden-, Lohnabrechnungs-, Inventar- und Zahlungslisten werden nach diesem Zeitraum gefiltert; andere Daten zeigen den aktuellen Stand.",
      "presets": { "last12Months": "Letzte 12 Monate", "thisYear": "Dieses Jahr", "allTime": "Gesamter Zeitraum", "custom": "Benutzerdefiniert" },
      "from": "Von",
      "to": "Bis",
      "invalidPeriod": "Das Ende darf nicht vor dem Beginn liegen.",
      "generate": "PDF erstellen",
      "failed": "Das PDF konnte nicht erstellt werden."
    },
```

- [ ] **Step 7: Testleri ve kontrolleri çalıştır.** `web/` içinde:
  - `pnpm exec vitest run src/components/employees/employee-card-pdf-dialog.test.tsx`
  - `pnpm exec tsc --noEmit`
  - `pnpm exec eslint src/components/employees/employee-card-pdf-dialog.tsx src/components/employees/employee-card-pdf-dialog.test.tsx src/pages/employees/employee-card.tsx src/services/employees.service.ts`

  Expected: testler PASS, tsc ve eslint hatasız. `tsc` başka oturumların yarım değişikliklerinden hata verirse yalnızca kendi dosyalarımızın hataları düzeltilir, diğerleri raporlanır.

- [ ] **Step 8: Commit.** Önce `git diff --stat web/src/pages/employees/employee-card.tsx web/src/services/employees.service.ts` ile yalnızca bizim değişikliğimiz olduğu doğrulanır. Başka bir oturumun hunk'ı varsa o dosya commit dışı bırakılır ve kullanıcıya not düşülür (`git add -p` interaktif olduğu için kullanılamaz).

```bash
git add web/src/services/employees.service.ts web/src/components/employees/employee-card-pdf-dialog.tsx web/src/components/employees/employee-card-pdf-dialog.test.tsx web/src/pages/employees/employee-card.tsx web/public/locales/tr/employees.json web/public/locales/en/employees.json web/public/locales/de/employees.json
git commit -m "feat(web): calisan kartindan sekme ve donem secimli PDF indirme"
```

---

### Task 10: Uçtan uca doğrulama, inceleme, handoff

**Files:**
- Create: `docs/takim/handoff/employee-card-pdf.md`
- Modify: `docs/analysis/kolayik-video/faz1a-durum-takibi.md` (yalnızca ilgili bir kalem varsa)

- [ ] **Step 1: Dokunulan tüm test projeleri ve architecture testi**

```bash
export PATH="/c/Program Files/dotnet:$PATH"
for p in Sense.Shared.Infrastructure.Tests Sense.Modules.Employee.Tests Sense.Modules.Leave.Tests Sense.Modules.Payroll.Tests Sense.Modules.Assets.Tests Sense.Modules.Documents.Tests Sense.Modules.Expense.Tests; do dotnet test tests/Modules/$p || echo "FAIL $p"; done
dotnet test tests/Sense.Tests.Architecture
```

Expected: çıktıda `FAIL` satırı yok, architecture testleri yeşil.

- [ ] **Step 2: Test kapısı.** `node .claude/takim/betikler/test-kapisi.mjs` çalıştırılır. Başka oturumların değişikliklerinden gelen kırmızılar raporlanır; testler atlanmaz, silinmez, filtrelenmez.

- [ ] **Step 3: Canlı doğrulama.**
  1. `api`, `git archive HEAD` ile temiz bir export'tan derlenir. Hafızadaki "compose build kirli ağaçta" kuralı geçerlidir.
  2. `web` çalışma ağacından derlenir.
  3. Ardından `docker compose up -d api web`.
  4. Tarayıcıda `http://localhost:13000/app/employees/<id>/general` açılır (demo: owner@demo.local).
  5. PDF ikonuna basılır. Diyalogda "Diğer" sekmesi olmamalı.
  6. "Tüm zamanlar" ile PDF oluşturulur. İndirilen dosyada başlık, bölüm bantları ve Türkçe karakterler kontrol edilir.
  7. Ayarlar → Modüller'den İzin modülü kapatılır ve PDF tekrar oluşturulur. İzinler ve Fazla Mesai bölümleri olmamalı. Sonra modül geri açılır.

- [ ] **Step 4: Bağımsız inceleme.** `takim-gozden-gecirici` ajanı spec ve bu plana karşı çalıştırılır. CRITICAL ve HIGH bulgular düzeltilir, yeniden doğrulanır.

- [ ] **Step 5: Handoff notu** (`docs/takim/handoff/employee-card-pdf.md`). İçeriği:
  - Ne yapıldı: dosya listesi ve endpoint'ler.
  - Bilinen sınırlamalar:
    - Leave ve overtime sorgularında `PagingDefaults.MaxPageSize` sınırı.
    - Expense durumu ham enum adı olarak basılıyor.
    - Own-card'da sendika, istihdam dönemi, bordro profili ve haciz emirleri yok (self-service sorguları yok).
    - Attendance ve eğitim bölümleri yok.
    - Fotoğraf yok.
  - Ajanın kullanıcı adına verdiği kararlar (spec §2).

  Faz takibinde ilgili bir kalem varsa, koda göre doğrulanarak güncellenir.

- [ ] **Step 6: Commit**

```bash
git add docs/takim/handoff/employee-card-pdf.md
git commit -m "docs: calisan karti PDF handoff notu"
```
