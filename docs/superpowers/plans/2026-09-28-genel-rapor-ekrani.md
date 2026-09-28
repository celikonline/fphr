# Genel rapor ekranı Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raporlar menüsünde seçilen rapor `/app/reports/:key` tam sayfasında tablo (grid) olarak açılsın; Excel bu sayfadaki **Excel indir** düğmesinden indirilsin; yeni bir `IReportProvider` eklendiğinde ekran kodu gerekmesin.

**Architecture:** Sağlayıcılara dokunulmaz. Kaydedilmiş `.xlsx`, yeni `IReportTableReader` portuyla (`IReportExporter`'ın tersi) `ExportTable`'lara okunur; Reporting modülü yeni `GET /v1/reports/executions/{id}/data` ucunda arama → sıralama → sayfalama uygulayıp döner. Web'de parametre formu ortak bileşene çıkarılır, yeni `report-viewer` sayfası raporu otomatik getirir ve `ReportDataGrid` ile gösterir; e-posta bildirimi `notify=false` ile kapatılır.

**Tech Stack:** .NET 10 (xUnit v3, Shouldly, NSubstitute, ClosedXML), React + Vite + react-router v7 + Mantine 9 + TanStack Query/Table, vitest + Testing Library, i18next (tr/en/de).

**Spec:** `docs/superpowers/specs/2026-09-28-genel-rapor-ekrani-design.md`

## Global Constraints

- Dal `a/rapor-ekrani` (worktree `.claude/worktrees/rapor-ekrani`); `main`'e doğrudan yazılmaz. Commit biçimi `<type>: <açıklama>` (feat, fix, refactor, docs, test, chore). Yalnız kendi dosyalarını `git add` et (`git add -A` yok).
- Reporting modülü yalnızca `*.Contracts` projelerine bağlı kalır; yeni paket referansı eklenmez (`Modules_TalkOnlyThroughContracts`).
- Sağlayıcılara (`*ReportProvider.cs`), izinlere ve veri kapsamı mantığına dokunulmaz. Yeni izin yok: veri ucu `reports.my.executions`.
- `RequestReportCommand.Notify` varsayılanı `true` (mevcut istemciler etkilenmez); yeni sayfa `false` gönderir.
- Filtre değerleri URL'de yalnızca `orgUnitIds`, `year`, `month`, `from`, `to`, `includeTerminated` anahtarlarıyla taşınır.
- API JSON: camelCase, enum'lar camelCase string (`"dateTime"`), null'lar atlanır — web eksik alanı null sayar.
- Hata kodları `SharedResource.resx` (tr) ve `SharedResource.en.resx` içinde birlikte eklenir (`ErrorResourceParityTests`). Web metinleri `web/public/locales/{tr,en,de}/reports.json` üçünde birden eklenir.
- Kod stili: değişmezlik (yeni nesne üret), erken dönüş, `console.log` yok, dosya < 800 satır, fonksiyon < 50 satır. Web'de Tailwind yalnız layout; bileşenler Mantine.
- UI koduna başlamadan `frontend-standards` skill'i çağrılır (Task 6, 7, 8).
- Komutlar (Git Bash, çalışma dizini worktree kökü): `export PATH="/c/Program Files/dotnet:/c/Program Files/nodejs:$HOME/bin:$PATH"; export DOTNET_CLI_UI_LANGUAGE=en`. .NET testi: `dotnet test <csproj> --filter "FullyQualifiedName~<Sınıf>"`. Web testi: `cd web && pnpm exec vitest run <dosya>`. Bare `dotnet`/`pnpm` bulunamazsa `~/.claude/projects/.../memory/project-windows-toolchain.md` içindeki Docker/shim tariflerini kullan.

## Review Focus

Spec'in söylediği ama hiçbir görevin doğal testinin yakalamadığı, kullanıcıya en çok dokunacak durumlar (her biri sahibi görevde bir testle sabitlenir):

1. **Baştaki sıfırlı metin** (örn. kimlik no `00123`): Excel'de metin hücresi sayıya çevrilmeden metin kalmalı → Task 1 testi.
2. **Boş rapor** (yalnız başlık satırı / hiç veri yok): hata değil, "kayıt yok" görünmeli, sayfalayıcı görünmemeli → Task 1 (reader) ve Task 7 (grid) testleri.
3. **Yalnız boşluktan oluşan arama** (`"   "`): filtre uygulanmamalı, tüm satırlar dönmeli → Task 2 testi.
4. **React StrictMode'da çift otomatik getirme**: sayfa açılışında yalnızca bir execution üretilmeli → Task 8 testi.
5. **Bayat/yabancı `?execution=`** (saklama süresi doldu ya da başkasının id'si): sayfa çökmemeli, adresten temizleyip filtreler tamsa yeniden üretmeli → Task 8 testi.

---

### Task 0: Hazırlık ve taban çizgisi

**Files:** yok (yalnız doğrulama)

- [ ] **Step 1: Worktree'de bağımlılıkları kur**

Run (worktree kökünde): `pnpm install --frozen-lockfile`
Expected: tamamlanır (`dtrace-provider` gibi opsiyonel bir derleme uyarısı önemsiz).

- [ ] **Step 2: Web taban testi**

Run: `cd web && pnpm exec vitest run src/pages/reports.test.tsx`
Expected: PASS (4 test).

- [ ] **Step 3: Backend taban testi**

Run: `dotnet test tests/Modules/Sense.Modules.Reporting.Tests/Sense.Modules.Reporting.Tests.csproj --filter "FullyQualifiedName~ReportingHandlerTests"`
Expected: PASS.

(Commit yok.)

---

### Task 1: `IReportTableReader` portu ve Excel okuyucusu

**Files:**
- Create: `src/Shared/Sense.Shared.Contracts/Export/IReportTableReader.cs`
- Create: `src/Shared/Sense.Shared.Infrastructure/Export/ExcelReportTableReader.cs`
- Modify: `src/Shared/Sense.Shared.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs:51`
- Test: `tests/Modules/Sense.Shared.Infrastructure.Tests/Export/ExcelReportTableReaderTests.cs`

**Interfaces:**
- Consumes: `ExportTable`, `ExportColumn`, `ExportColumnType` (Shared.Contracts.Export), `ExcelReportExporter` (testte gidiş-dönüş için).
- Produces: `IReportTableReader.Read(byte[] content, CancellationToken cancellationToken = default) : IReadOnlyList<ExportTable>`; geçersiz içerikte `InvalidDataException`. Hücre değer tipleri: `string`, `double`, `bool`, `DateOnly`, `DateTime`, `null`; sütun tipi `ExportColumnType`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/Modules/Sense.Shared.Infrastructure.Tests/Export/ExcelReportTableReaderTests.cs`:

```csharp
using ClosedXML.Excel;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Infrastructure.Export;
using Shouldly;
using Xunit;

namespace Sense.Shared.Infrastructure.Tests.Export;

/// <summary>Genel rapor ekranı: <see cref="IReportExporter"/>'ın ürettiği xlsx'i tabloya geri okuyan port.</summary>
public sealed class ExcelReportTableReaderTests
{
    private readonly ExcelReportExporter _exporter = new();
    private readonly ExcelReportTableReader _reader = new();

    [Fact]
    public void Read_RoundTripsTypedColumnsFromTheExporter()
    {
        var table = new ExportTable(
            "Rapor",
            [
                new ExportColumn("Ad"),
                new ExportColumn("Maaş", ExportColumnType.Number),
                new ExportColumn("İşe giriş", ExportColumnType.Date),
                new ExportColumn("Talep", ExportColumnType.DateTime),
                new ExportColumn("Aktif", ExportColumnType.Boolean),
            ],
            [
                ["Ayşe", 45000.5, new DateOnly(2024, 3, 1), new DateTime(2024, 3, 1, 14, 30, 0), true],
                ["Mehmet", 38000, new DateOnly(2023, 11, 15), new DateTime(2023, 11, 15, 9, 5, 0), false],
            ]);
        var file = _exporter.ExportToExcel(table, TestContext.Current.CancellationToken);

        var result = _reader.Read(file.Content, TestContext.Current.CancellationToken);

        var sheet = result.ShouldHaveSingleItem();
        sheet.SheetName.ShouldBe("Rapor");
        sheet.Columns.Select(c => c.Header).ShouldBe(["Ad", "Maaş", "İşe giriş", "Talep", "Aktif"]);
        sheet.Columns.Select(c => c.Type).ShouldBe(
            [ExportColumnType.Text, ExportColumnType.Number, ExportColumnType.Date, ExportColumnType.DateTime, ExportColumnType.Boolean]);
        sheet.Rows[0].ShouldBe(new object?[] { "Ayşe", 45000.5, new DateOnly(2024, 3, 1), new DateTime(2024, 3, 1, 14, 30, 0), true });
        sheet.Rows[1].ShouldBe(new object?[] { "Mehmet", 38000.0, new DateOnly(2023, 11, 15), new DateTime(2023, 11, 15, 9, 5, 0), false });
    }

    [Fact]
    public void Read_KeepsBlankCellsNull_AndSkipsFullyEmptyRows()
    {
        var table = new ExportTable("Sayfa", [new ExportColumn("A"), new ExportColumn("B")], [["a", null], [null, null], ["b", "x"]]);
        var file = _exporter.ExportToExcel(table, TestContext.Current.CancellationToken);

        var sheet = _reader.Read(file.Content, TestContext.Current.CancellationToken).Single();

        sheet.Rows.Count.ShouldBe(2);
        sheet.Rows[0].ShouldBe(new object?[] { "a", null });
        sheet.Rows[1].ShouldBe(new object?[] { "b", "x" });
    }

    [Fact]
    public void Read_HeaderOnlySheet_ReturnsColumnsAndNoRows()
    {
        var table = new ExportTable("Boş", [new ExportColumn("A"), new ExportColumn("B")], []);
        var file = _exporter.ExportToExcel(table, TestContext.Current.CancellationToken);

        var sheet = _reader.Read(file.Content, TestContext.Current.CancellationToken).Single();

        sheet.Columns.Select(c => c.Header).ShouldBe(["A", "B"]);
        sheet.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void Read_MultipleSheets_KeepsOrderAndNames()
    {
        var file = _exporter.ExportToExcel(
            "iki.xlsx",
            [new ExportTable("Birinci", [new ExportColumn("X")], [["1"]]), new ExportTable("İkinci", [new ExportColumn("Y")], [["2"]])],
            TestContext.Current.CancellationToken);

        var sheets = _reader.Read(file.Content, TestContext.Current.CancellationToken);

        sheets.Select(s => s.SheetName).ShouldBe(["Birinci", "İkinci"]);
    }

    [Fact]
    public void Read_MixedTypesInOneColumn_CollapseToText()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Karışık");
        worksheet.Cell(1, 1).Value = "Değer";
        worksheet.Cell(2, 1).Value = 5;
        worksheet.Cell(3, 1).Value = "metin";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var sheet = _reader.Read(stream.ToArray(), TestContext.Current.CancellationToken).Single();

        sheet.Columns[0].Type.ShouldBe(ExportColumnType.Text);
        sheet.Rows.Select(r => r[0]).ShouldBe(new object?[] { "5", "metin" });
    }

    [Fact]
    public void Read_TextCellWithLeadingZeros_StaysText()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Kimlik");
        worksheet.Cell(1, 1).Value = "No";
        worksheet.Cell(2, 1).Value = "00123";
        worksheet.Cell(2, 1).DataType = XLDataType.Text;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var sheet = _reader.Read(stream.ToArray(), TestContext.Current.CancellationToken).Single();

        sheet.Columns[0].Type.ShouldBe(ExportColumnType.Text);
        sheet.Rows[0][0].ShouldBe("00123");
    }

    [Fact]
    public void Read_NotAWorkbook_ThrowsInvalidData() =>
        Should.Throw<InvalidDataException>(() => _reader.Read([1, 2, 3], TestContext.Current.CancellationToken));
}
```

- [ ] **Step 2: Testin derlenmediğini/başarısız olduğunu doğrula**

Run: `dotnet test tests/Modules/Sense.Shared.Infrastructure.Tests/Sense.Shared.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ExcelReportTableReaderTests"`
Expected: FAIL — `ExcelReportTableReader` tanımlı değil (derleme hatası).

- [ ] **Step 3: Portu yaz**

`src/Shared/Sense.Shared.Contracts/Export/IReportTableReader.cs`:

```csharp
namespace Sense.Shared.Contracts.Export;

/// <summary>
/// <see cref="IReportExporter"/>'ın tersi: bir xlsx'i sayfa/sütun/satır tablolarına geri okur (genel rapor ekranı).
/// Sayfanın 1. satırı başlıktır; hücre değerleri <see cref="string"/>, <see cref="double"/>, <see cref="bool"/>,
/// <see cref="DateOnly"/>, <see cref="DateTime"/> ya da <c>null</c> (boş hücre) olur. Sütun tipi, sütunun dolu
/// hücrelerinin ortak tipidir; karışıksa <see cref="ExportColumnType.Text"/> (değerler metne çevrilir).
/// </summary>
public interface IReportTableReader
{
    /// <exception cref="InvalidDataException">İçerik okunabilir bir xlsx çalışma kitabı değilse.</exception>
    IReadOnlyList<ExportTable> Read(byte[] content, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Okuyucuyu yaz**

`src/Shared/Sense.Shared.Infrastructure/Export/ExcelReportTableReader.cs`:

```csharp
using System.Globalization;
using ClosedXML.Excel;
using Sense.Shared.Contracts.Export;

namespace Sense.Shared.Infrastructure.Export;

/// <summary>ClosedXML tabanlı <see cref="IReportTableReader"/>; <see cref="ExcelReportExporter"/>'ın yazdığı biçimi (1. satır başlık) okur.</summary>
public sealed class ExcelReportTableReader : IReportTableReader
{
    private const int HeaderRow = 1;

    public IReadOnlyList<ExportTable> Read(byte[] content, CancellationToken cancellationToken = default)
    {
        try
        {
            using var workbook = new XLWorkbook(new MemoryStream(content));
            var tables = new List<ExportTable>();
            foreach (var sheet in workbook.Worksheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tables.Add(ReadSheet(sheet));
            }

            return tables;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException("Dosya okunabilir bir Excel çalışma kitabı değil.", ex);
        }
    }

    private static ExportTable ReadSheet(IXLWorksheet sheet)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;

        var rows = new List<object?[]>();
        for (var r = HeaderRow + 1; r <= lastRow; r++)
        {
            var cells = new object?[lastColumn];
            for (var c = 0; c < lastColumn; c++)
            {
                cells[c] = ReadValue(sheet.Cell(r, c + 1));
            }

            if (cells.Any(value => value is not null))
            {
                rows.Add(cells);
            }
        }

        var columns = new List<ExportColumn>(lastColumn);
        for (var c = 0; c < lastColumn; c++)
        {
            var type = ColumnTypeOf(rows, c);
            columns.Add(new ExportColumn(sheet.Cell(HeaderRow, c + 1).GetString(), type));
            if (type == ExportColumnType.Text)
            {
                foreach (var row in rows)
                {
                    row[c] = row[c] is null or string ? row[c] : ToText(row[c]!);
                }
            }
        }

        return new ExportTable(sheet.Name, columns, rows.Select(row => (IReadOnlyList<object?>)row).ToList());
    }

    private static object? ReadValue(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        switch (cell.DataType)
        {
            case XLDataType.Boolean:
                return cell.GetBoolean();
            case XLDataType.Number:
                return cell.GetDouble();
            case XLDataType.DateTime:
            {
                var dateTime = cell.GetDateTime();
                return HasTimeFormat(cell) ? dateTime : DateOnly.FromDateTime(dateTime);
            }

            default:
            {
                var text = cell.GetString();
                return text.Length == 0 ? null : text;
            }
        }
    }

    /// <summary><see cref="ExcelReportExporter"/> tarih-saat hücrelerine "dd.mm.yyyy hh:mm", yalnız tarihe "dd.mm.yyyy" biçimi yazar.</summary>
    private static bool HasTimeFormat(IXLCell cell) =>
        cell.Style.NumberFormat.Format.Contains('h', StringComparison.OrdinalIgnoreCase);

    private static ExportColumnType ColumnTypeOf(List<object?[]> rows, int column)
    {
        ExportColumnType? type = null;
        foreach (var row in rows)
        {
            if (row[column] is not { } value)
            {
                continue;
            }

            var current = TypeOf(value);
            if (type is null)
            {
                type = current;
            }
            else if (type != current)
            {
                return ExportColumnType.Text;
            }
        }

        return type ?? ExportColumnType.Text;
    }

    private static ExportColumnType TypeOf(object value) => value switch
    {
        bool => ExportColumnType.Boolean,
        double => ExportColumnType.Number,
        DateOnly => ExportColumnType.Date,
        DateTime => ExportColumnType.DateTime,
        _ => ExportColumnType.Text,
    };

    private static string ToText(object value) => value switch
    {
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        double number => number.ToString("0.############", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
```

- [ ] **Step 5: DI kaydı**

`InfrastructureServiceCollectionExtensions.cs` içinde `services.TryAddSingleton<IReportExporter, ExcelReportExporter>();` satırının hemen altına ekle:

```csharp
        services.TryAddSingleton<IReportTableReader, ExcelReportTableReader>();
```

- [ ] **Step 6: Testin geçtiğini doğrula**

Run: `dotnet test tests/Modules/Sense.Shared.Infrastructure.Tests/Sense.Shared.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ExcelReportTableReaderTests"`
Expected: PASS (7 test). `Read_TextCellWithLeadingZeros_StaysText` ClosedXML davranışı yüzünden düşerse okuyucuda `cell.DataType` yerine hücrenin ham tipine bak; hücre gerçekten Excel'de metinse `"00123"` metin kalmalıdır — çözümü okuyucuda yap, testi gevşetme.

- [ ] **Step 7: Commit**

```bash
git add src/Shared/Sense.Shared.Contracts/Export/IReportTableReader.cs src/Shared/Sense.Shared.Infrastructure/Export/ExcelReportTableReader.cs src/Shared/Sense.Shared.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs tests/Modules/Sense.Shared.Infrastructure.Tests/Export/ExcelReportTableReaderTests.cs
git commit -m "feat(reporting): xlsx'i tabloya okuyan IReportTableReader portu"
```

---

### Task 2: `ReportTableView` — arama, sıralama, sayfalama

**Files:**
- Create: `src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportTableView.cs`
- Test: `tests/Modules/Sense.Modules.Reporting.Tests/ReportTableViewTests.cs`

**Interfaces:**
- Consumes: `ExportTable`, `ExportColumnType` (Task 1), `PagedResult<T>` (Shared.Contracts.Paging).
- Produces: `ReportTableView.Build(ExportTable table, string? search, int? sortColumn, bool descending, int page, int pageSize) : PagedResult<IReadOnlyList<object?>>` (`page ≥ 1`, `pageSize ≥ 1`).

- [ ] **Step 1: Başarısız testleri yaz**

`tests/Modules/Sense.Modules.Reporting.Tests/ReportTableViewTests.cs`:

```csharp
using Sense.Modules.Reporting.Application;
using Sense.Shared.Contracts.Export;
using Shouldly;
using Xunit;

namespace Sense.Modules.Reporting.Tests;

public sealed class ReportTableViewTests
{
    private static ExportTable People(params object?[][] rows) =>
        new("Kişiler", [new ExportColumn("Ad"), new ExportColumn("Maaş", ExportColumnType.Number)], rows.Select(r => (IReadOnlyList<object?>)r).ToList());

    private static IEnumerable<object?> Names(IEnumerable<IReadOnlyList<object?>> rows) => rows.Select(r => r[0]);

    [Fact]
    public void Build_Search_FoldsTurkishCaseAndDiacritics()
    {
        var table = People(["İzin Yönetimi", 1.0], ["Çalışan", 2.0], ["Bordro", 3.0]);

        Names(ReportTableView.Build(table, "izin", null, false, 1, 50).Items).ShouldBe(["İzin Yönetimi"]);
        Names(ReportTableView.Build(table, "CALISAN", null, false, 1, 50).Items).ShouldBe(["Çalışan"]);
    }

    [Fact]
    public void Build_WhitespaceOnlySearch_DoesNotFilter()
    {
        var table = People(["Ayşe", 1.0], ["Mehmet", 2.0]);

        var result = ReportTableView.Build(table, "   ", null, false, 1, 50);

        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public void Build_SearchMatchesNumbersAndDatesInAnyColumn()
    {
        var table = new ExportTable(
            "Kayıt",
            [new ExportColumn("Ad"), new ExportColumn("Tarih", ExportColumnType.Date)],
            [["a", new DateOnly(2024, 3, 1)], ["b", new DateOnly(2023, 11, 15)]]);

        Names(ReportTableView.Build(table, "01.03.2024", null, false, 1, 50).Items).ShouldBe(["a"]);
        Names(ReportTableView.Build(table, "2023-11-15", null, false, 1, 50).Items).ShouldBe(["b"]);
    }

    [Fact]
    public void Build_SortsNumbersAscendingAndDescending_WithBlanksLast()
    {
        var table = People(["a", 20.0], ["b", null], ["c", 5.0], ["d", 100.0]);

        Names(ReportTableView.Build(table, null, 1, false, 1, 50).Items).ShouldBe(["c", "a", "d", "b"]);
        Names(ReportTableView.Build(table, null, 1, true, 1, 50).Items).ShouldBe(["d", "a", "c", "b"]);
    }

    [Fact]
    public void Build_SortsTextWithTurkishCollation()
    {
        var table = People(["Zeynep", 1.0], ["Çağla", 2.0], ["Ali", 3.0]);

        Names(ReportTableView.Build(table, null, 0, false, 1, 50).Items).ShouldBe(["Ali", "Çağla", "Zeynep"]);
    }

    [Fact]
    public void Build_Pages_AndReportsTotalOfTheFilteredSet()
    {
        var table = People(["1", 1.0], ["2", 2.0], ["3", 3.0], ["4", 4.0], ["5", 5.0]);

        var second = ReportTableView.Build(table, null, null, false, 2, 2);

        Names(second.Items).ShouldBe(["3", "4"]);
        second.TotalCount.ShouldBe(5);
        second.Page.ShouldBe(2);
        second.PageSize.ShouldBe(2);
    }

    [Fact]
    public void Build_PageBeyondTheEnd_ReturnsNoItemsButKeepsTotal()
    {
        var table = People(["1", 1.0], ["2", 2.0]);

        var result = ReportTableView.Build(table, null, null, false, 9, 50);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public void Build_EmptyTable_ReturnsEmptyResult()
    {
        var result = ReportTableView.Build(People(), null, 0, false, 1, 50);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula**

Run: `dotnet test tests/Modules/Sense.Modules.Reporting.Tests/Sense.Modules.Reporting.Tests.csproj --filter "FullyQualifiedName~ReportTableViewTests"`
Expected: FAIL — `ReportTableView` tanımlı değil.

- [ ] **Step 3: Uygula**

`src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportTableView.cs`:

```csharp
using System.Globalization;
using System.Text;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Paging;

namespace Sense.Modules.Reporting.Application;

/// <summary>Okunmuş bir rapor sayfasını ekran için arama → sıralama → sayfalama sırasıyla hazırlar (bellekte; dosya zaten ayrıştırılmıştır).</summary>
public static class ReportTableView
{
    private static readonly StringComparer TextComparer = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    public static PagedResult<IReadOnlyList<object?>> Build(ExportTable table, string? search, int? sortColumn, bool descending, int page, int pageSize)
    {
        IEnumerable<IReadOnlyList<object?>> rows = table.Rows;

        var needle = Fold((search ?? string.Empty).Trim());
        if (needle.Length > 0)
        {
            rows = rows.Where(row => row.Any(cell => cell is not null && Fold(CellText(cell)).Contains(needle, StringComparison.Ordinal)));
        }

        if (sortColumn is { } column)
        {
            rows = Sort(rows, column, descending);
        }

        var matched = rows.ToList();
        var skip = (long)(page - 1) * pageSize;
        var items = matched.Skip(skip > int.MaxValue ? int.MaxValue : (int)skip).Take(pageSize).ToList();
        return new PagedResult<IReadOnlyList<object?>>(items, page, pageSize, matched.Count);
    }

    private static IEnumerable<IReadOnlyList<object?>> Sort(IEnumerable<IReadOnlyList<object?>> rows, int column, bool descending)
    {
        var list = rows.ToList();
        var withValue = list.Where(row => Cell(row, column) is not null);
        var withoutValue = list.Where(row => Cell(row, column) is null);
        var ordered = descending
            ? withValue.OrderByDescending(row => Cell(row, column), CellComparer.Instance)
            : withValue.OrderBy(row => Cell(row, column), CellComparer.Instance);
        return ordered.Concat(withoutValue);
    }

    private static object? Cell(IReadOnlyList<object?> row, int column) => column < row.Count ? row[column] : null;

    private static string CellText(object value) => value switch
    {
        string text => text,
        DateOnly date => $"{date:dd.MM.yyyy} {date:yyyy-MM-dd}",
        DateTime dateTime => $"{dateTime:dd.MM.yyyy HH:mm} {dateTime:yyyy-MM-dd HH:mm}",
        bool flag => flag ? "true" : "false",
        double number => number.ToString("0.############", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <summary>Küçük harfe çevirir ve Türkçe I/İ/ı ile aksanları eşitler ("İzin" ↔ "izin", "Çalışan" ↔ "calisan"); web katalog aramasındaki <c>fold</c> ile aynı kural.</summary>
    private static string Fold(string value)
    {
        var lowered = value.Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i').ToLowerInvariant();
        var builder = new StringBuilder(lowered.Length);
        foreach (var ch in lowered.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Sütun tek tipli olduğundan (okuyucu garanti eder) aynı tipleri kendi sırasıyla, metni Türkçe harf sırasıyla karşılaştırır.</summary>
    private sealed class CellComparer : IComparer<object?>
    {
        public static readonly CellComparer Instance = new();

        public int Compare(object? x, object? y) => (x, y) switch
        {
            (string a, string b) => TextComparer.Compare(a, b),
            (IComparable a, { } b) when a.GetType() == b.GetType() => a.CompareTo(b),
            _ => TextComparer.Compare(CellText(x!), CellText(y!)),
        };
    }
}
```

- [ ] **Step 4: Testin geçtiğini doğrula**

Run: `dotnet test tests/Modules/Sense.Modules.Reporting.Tests/Sense.Modules.Reporting.Tests.csproj --filter "FullyQualifiedName~ReportTableViewTests"`
Expected: PASS (7 test).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportTableView.cs tests/Modules/Sense.Modules.Reporting.Tests/ReportTableViewTests.cs
git commit -m "feat(reporting): rapor tablosu icin arama/siralama/sayfalama gorunumu"
```

---

### Task 3: `GetReportExecutionDataQuery`, DTO'lar ve hata kodları

**Files:**
- Modify: `src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportExecutionDtos.cs`
- Modify: `src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportingErrors.cs`
- Create: `src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportDataQuery.cs`
- Modify: `src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx` (tr), `SharedResource.en.resx`
- Test: `tests/Modules/Sense.Modules.Reporting.Tests/ReportDataHandlerTests.cs`

**Interfaces:**
- Consumes: `IReportTableReader` (Task 1), `ReportTableView.Build` (Task 2), `IReportExecutionRepository.GetByIdAsync`, `ICurrentUser`.
- Produces:
  - `GetReportExecutionDataQuery(Guid Id, int Sheet, PagedQuery Paging) : IQuery<ReportDataDto>`; sıralama `Paging.Sort` içinde sütun indeksi (`"2"` artan, `"-2"` azalan), arama `Paging.Q`.
  - `ReportSheetDto(int Index, string Name)`, `ReportColumnDto(string Header, ExportColumnType Type)`, `ReportDataDto(IReadOnlyList<ReportSheetDto> Sheets, int SheetIndex, IReadOnlyList<ReportColumnDto> Columns, PagedResult<IReadOnlyList<object?>> Rows)`.
  - Hata kodları: `ReportingErrors.PreviewNotAvailable` (`report_execution.preview_not_available`), `SheetInvalid` (`report_execution.sheet_invalid`), `SortColumnInvalid` (`report_execution.sort_column_invalid`).

- [ ] **Step 1: Başarısız testleri yaz**

`tests/Modules/Sense.Modules.Reporting.Tests/ReportDataHandlerTests.cs`:

```csharp
using NSubstitute;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Infrastructure.Export;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Reporting.Tests;

public sealed class ReportDataHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static ICurrentUser CurrentUser()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        return currentUser;
    }

    private static byte[] Workbook(params ExportTable[] sheets) =>
        new ExcelReportExporter().ExportToExcel("rapor.xlsx", sheets, Ct).Content;

    private static ExportTable People() => new(
        "Kişiler",
        [new ExportColumn("Ad"), new ExportColumn("Maaş", ExportColumnType.Number)],
        [["Ayşe", 30.0], ["Mehmet", 10.0], ["Zeynep", 20.0]]);

    private static ReportExecution Succeeded(byte[] content, string contentType = ExcelReportExporter.ExcelContentType, Guid? owner = null)
    {
        var execution = ReportExecution.Create(TenantId, "employee.list", null, owner ?? UserId, DateTime.UtcNow).Value;
        execution.Start(DateTime.UtcNow);
        execution.Complete("rapor.xlsx", contentType, content, DateTime.UtcNow);
        return execution;
    }

    private static GetReportExecutionDataHandler HandlerFor(ReportExecution execution)
    {
        var executions = Substitute.For<IReportExecutionRepository>();
        executions.GetByIdAsync(execution.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(execution));
        return new GetReportExecutionDataHandler(executions, CurrentUser(), new ExcelReportTableReader());
    }

    private static Task<Result<ReportDataDto>> Run(GetReportExecutionDataHandler handler, ReportExecution execution, int sheet = 0, PagedQuery? paging = null) =>
        handler.Handle(new GetReportExecutionDataQuery(execution.Id, sheet, paging ?? new PagedQuery()), Ct);

    [Fact]
    public async Task Owner_GetsSheetsColumnsAndFirstPageOfRows()
    {
        var execution = Succeeded(Workbook(People()));

        var result = await Run(HandlerFor(execution), execution);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Sheets.Select(s => s.Name).ShouldBe(["Kişiler"]);
        result.Value.SheetIndex.ShouldBe(0);
        result.Value.Columns.Select(c => (c.Header, c.Type)).ShouldBe([("Ad", ExportColumnType.Text), ("Maaş", ExportColumnType.Number)]);
        result.Value.Rows.TotalCount.ShouldBe(3);
        result.Value.Rows.Items.Select(r => r[0]).ShouldBe(["Ayşe", "Mehmet", "Zeynep"]);
    }

    [Fact]
    public async Task SearchSortAndPaging_AreAppliedTogether()
    {
        var execution = Succeeded(Workbook(People()));
        var paging = new PagedQuery { Page = 1, PageSize = 1, Sort = "-1", Q = "e" };

        var result = await Run(HandlerFor(execution), execution, paging: paging);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.TotalCount.ShouldBe(3);
        result.Value.Rows.Items.Single()[0].ShouldBe("Ayşe");
    }

    [Fact]
    public async Task SecondSheet_IsReadWhenRequested()
    {
        var other = new ExportTable("Diğer", [new ExportColumn("X")], [["1"]]);
        var execution = Succeeded(Workbook(People(), other));

        var result = await Run(HandlerFor(execution), execution, sheet: 1);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SheetIndex.ShouldBe(1);
        result.Value.Columns.Single().Header.ShouldBe("X");
    }

    [Fact]
    public async Task HeaderOnlyReport_ReturnsColumnsAndNoRows()
    {
        var execution = Succeeded(Workbook(new ExportTable("Boş", [new ExportColumn("A")], [])));

        var result = await Run(HandlerFor(execution), execution);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Columns.Count.ShouldBe(1);
        result.Value.Rows.Items.ShouldBeEmpty();
        result.Value.Rows.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task LargeReport_IsSlicedToThePageSize()
    {
        var rows = Enumerable.Range(1, 5000).Select(i => (IReadOnlyList<object?>)new object?[] { $"kişi {i}", (double)i }).ToList();
        var execution = Succeeded(Workbook(new ExportTable("Büyük", [new ExportColumn("Ad"), new ExportColumn("No", ExportColumnType.Number)], rows)));

        var result = await Run(HandlerFor(execution), execution, paging: new PagedQuery { Page = 3, PageSize = 50 });

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.TotalCount.ShouldBe(5000);
        result.Value.Rows.Items.Count.ShouldBe(50);
        result.Value.Rows.Items[0][0].ShouldBe("kişi 101");
    }

    [Fact]
    public async Task WhenNotOwner_FailsForbidden()
    {
        var execution = Succeeded(Workbook(People()), owner: Guid.NewGuid());

        var result = await Run(HandlerFor(execution), execution);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.NotOwner);
    }

    [Fact]
    public async Task WhenNotYetSucceeded_FailsNotReady()
    {
        var execution = ReportExecution.Create(TenantId, "employee.list", null, UserId, DateTime.UtcNow).Value;
        execution.Start(DateTime.UtcNow);

        var result = await Run(HandlerFor(execution), execution);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.ReportNotReady);
    }

    [Fact]
    public async Task WhenFileIsNotXlsx_FailsPreviewNotAvailable()
    {
        var execution = Succeeded([1, 2, 3], contentType: "text/csv");

        var result = await Run(HandlerFor(execution), execution);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.PreviewNotAvailable);
    }

    [Fact]
    public async Task WhenXlsxIsCorrupt_FailsPreviewNotAvailable()
    {
        var execution = Succeeded([1, 2, 3]);

        var result = await Run(HandlerFor(execution), execution);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.PreviewNotAvailable);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task WhenSheetOutOfRange_FailsSheetInvalid(int sheet)
    {
        var execution = Succeeded(Workbook(People()));

        var result = await Run(HandlerFor(execution), execution, sheet);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.SheetInvalid);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("-9")]
    [InlineData("ad")]
    public async Task WhenSortColumnInvalid_FailsSortColumnInvalid(string sort)
    {
        var execution = Succeeded(Workbook(People()));

        var result = await Run(HandlerFor(execution), execution, paging: new PagedQuery { Sort = sort });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ReportingErrors.SortColumnInvalid);
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula**

Run: `dotnet test tests/Modules/Sense.Modules.Reporting.Tests/Sense.Modules.Reporting.Tests.csproj --filter "FullyQualifiedName~ReportDataHandlerTests"`
Expected: FAIL — `GetReportExecutionDataHandler`, `ReportDataDto`, yeni `ReportingErrors` üyeleri tanımlı değil.

- [ ] **Step 3: Hata kodlarını ekle**

`ReportingErrors.cs` içinde `ParameterInvalid` sabitinin altına (sınıf kapanmadan önce) ekle:

```csharp

    /// <summary>The execution's file cannot be shown as a grid (not an .xlsx, or unreadable); the web offers the download only.</summary>
    public const string PreviewNotAvailable = "report_execution.preview_not_available";

    /// <summary>The requested sheet index does not exist in the workbook.</summary>
    public const string SheetInvalid = "report_execution.sheet_invalid";

    /// <summary>The requested sort column index is not a column of the sheet.</summary>
    public const string SortColumnInvalid = "report_execution.sort_column_invalid";
```

`SharedResource.resx` (tr): `<data name="report_execution.scope_empty" ...>` satırının hemen üstüne ekle:

```xml
  <data name="report_execution.preview_not_available" xml:space="preserve"><value>Bu rapor ekranda gösterilemiyor; Excel olarak indirebilirsiniz.</value></data>
  <data name="report_execution.sheet_invalid" xml:space="preserve"><value>Rapor sayfası bulunamadı.</value></data>
  <data name="report_execution.sort_column_invalid" xml:space="preserve"><value>Sıralanacak sütun geçersiz.</value></data>
```

`SharedResource.en.resx`: aynı yere:

```xml
  <data name="report_execution.preview_not_available" xml:space="preserve"><value>This report cannot be shown on screen; you can download it as Excel.</value></data>
  <data name="report_execution.sheet_invalid" xml:space="preserve"><value>Report sheet not found.</value></data>
  <data name="report_execution.sort_column_invalid" xml:space="preserve"><value>The sort column is invalid.</value></data>
```

- [ ] **Step 4: DTO'ları ekle**

`ReportExecutionDtos.cs` dosyasının `using` bloğuna `using Sense.Shared.Contracts.Export;` ve `using Sense.Shared.Contracts.Paging;` ekle, dosyanın sonuna ekle:

```csharp

/// <summary>One sheet of the report workbook (sheet tabs on the web).</summary>
public sealed record ReportSheetDto(int Index, string Name);

/// <summary>Grid column: the header text and the value type the web formats by ("text", "number", "date", "dateTime", "boolean").</summary>
public sealed record ReportColumnDto(string Header, ExportColumnType Type);

/// <summary>
/// Grid data of a report execution's workbook: every sheet, the requested sheet's columns and one filtered/sorted/paged
/// slice of its rows. Cells are string / number / bool / ISO date ("yyyy-MM-dd") / ISO date-time / null, in column order.
/// </summary>
public sealed record ReportDataDto(
    IReadOnlyList<ReportSheetDto> Sheets,
    int SheetIndex,
    IReadOnlyList<ReportColumnDto> Columns,
    PagedResult<IReadOnlyList<object?>> Rows);
```

- [ ] **Step 5: Sorgu ve işleyiciyi yaz**

`src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportDataQuery.cs`:

```csharp
using System.Globalization;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Reporting.Application;

/// <summary>
/// Self-service: a succeeded execution's workbook as grid data (generic report screen). <c>Paging.Q</c> searches every cell,
/// <c>Paging.Sort</c> is a single column index ("2" ascending, "-2" descending), <c>Sheet</c> is the 0-based sheet index.
/// </summary>
[RequiresPermission("reports.my.executions")]
public sealed record GetReportExecutionDataQuery(Guid Id, int Sheet, PagedQuery Paging) : IQuery<ReportDataDto>;

public sealed class GetReportExecutionDataHandler(IReportExecutionRepository executions, ICurrentUser currentUser, IReportTableReader reader)
    : IQueryHandler<GetReportExecutionDataQuery, ReportDataDto>
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<Result<ReportDataDto>> Handle(GetReportExecutionDataQuery query, CancellationToken cancellationToken)
    {
        var execution = await executions.GetByIdAsync(query.Id, cancellationToken);
        if (execution.IsFailure)
        {
            return execution.Error;
        }

        if (execution.Value.RequestedByUserId != (currentUser.UserId ?? Guid.Empty))
        {
            return Error.Forbidden(ReportingErrors.NotOwner);
        }

        if (execution.Value.Status != ReportExecutionStatus.Succeeded || execution.Value.ResultContent is null)
        {
            return Error.Validation(ReportingErrors.ReportNotReady);
        }

        if (execution.Value.ResultContentType != XlsxContentType)
        {
            return Error.Validation(ReportingErrors.PreviewNotAvailable);
        }

        IReadOnlyList<ExportTable> tables;
        try
        {
            tables = reader.Read(execution.Value.ResultContent, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Error.Validation(ReportingErrors.PreviewNotAvailable);
        }

        if (query.Sheet < 0 || query.Sheet >= tables.Count)
        {
            return Error.Validation(ReportingErrors.SheetInvalid);
        }

        var table = tables[query.Sheet];
        var sortClause = query.Paging.SortClauses.FirstOrDefault();
        int? sortColumn = null;
        if (sortClause is not null)
        {
            if (!int.TryParse(sortClause.Field, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= table.Columns.Count)
            {
                return Error.Validation(ReportingErrors.SortColumnInvalid);
            }

            sortColumn = index;
        }

        var rows = ReportTableView.Build(table, query.Paging.Q, sortColumn, sortClause?.Descending ?? false, query.Paging.Page, query.Paging.PageSize);
        return new ReportDataDto(
            tables.Select((t, i) => new ReportSheetDto(i, t.SheetName)).ToList(),
            query.Sheet,
            table.Columns.Select(c => new ReportColumnDto(c.Header, c.Type)).ToList(),
            rows);
    }
}
```

- [ ] **Step 6: Testin geçtiğini doğrula**

Run: `dotnet test tests/Modules/Sense.Modules.Reporting.Tests/Sense.Modules.Reporting.Tests.csproj --filter "FullyQualifiedName~ReportDataHandlerTests"`
Expected: PASS (14 test: 1+1+1+1+1+1+1+1+1+2+3). Sonra `dotnet test tests/Modules/Sense.Shared.Infrastructure.Tests/Sense.Shared.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ErrorResourceParityTests"` → PASS (tr/en eşitliği).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportDataQuery.cs src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportExecutionDtos.cs src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportingErrors.cs src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.resx src/Shared/Sense.Shared.Infrastructure/Resources/SharedResource.en.resx tests/Modules/Sense.Modules.Reporting.Tests/ReportDataHandlerTests.cs
git commit -m "feat(reporting): rapor sonucunu grid verisi olarak donen sorgu"
```

---

### Task 4: `notify` bayrağı, API ucu ve spec düzeltmesi

**Files:**
- Modify: `src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportCommands.cs`
- Modify: `src/Modules/Reporting/Sense.Modules.Reporting.Api/Controllers/ReportsController.cs`
- Modify: `docs/superpowers/specs/2026-09-28-genel-rapor-ekrani-design.md`
- Test: `tests/Modules/Sense.Modules.Reporting.Tests/ReportingHandlerTests.cs`

**Interfaces:**
- Consumes: `GetReportExecutionDataQuery` (Task 3).
- Produces: `RequestReportCommand(string ReportKey, Dictionary<string,string>? Parameters, bool Notify = true)`; `RequestReportRequest(string ReportKey, Dictionary<string,string>? Parameters, bool? Notify = null)`; `GET /v1/reports/executions/{id}/data?sheet&page&pageSize&q&sort → ReportDataDto`.

- [ ] **Step 1: Başarısız testi yaz**

`ReportingHandlerTests.cs` içinde `RequestReportHandler_WithKnownProvider_ResolvesAndCompletesSynchronously` testinin hemen altına ekle:

```csharp
    [Fact]
    public async Task RequestReportHandler_WhenNotifyIsFalse_CompletesWithoutSendingTheReadyEmail()
    {
        var provider = FakeProvider("documents.missingDocuments");
        var mailer = Substitute.For<IReportMailer>();
        var handler = new RequestReportHandler([provider], Substitute.For<IReportExecutionRepository>(), mailer, TenantContext(), CurrentUser(), Substitute.For<IPermissionService>(), TimeProvider.System);

        var result = await handler.Handle(new RequestReportCommand("documents.missingDocuments", null, Notify: false), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ReportExecutionStatus.Succeeded);
        await mailer.DidNotReceive().SendReportReadyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Başarısız olduğunu doğrula**

Run: `dotnet test tests/Modules/Sense.Modules.Reporting.Tests/Sense.Modules.Reporting.Tests.csproj --filter "FullyQualifiedName~RequestReportHandler_WhenNotifyIsFalse"`
Expected: FAIL — `RequestReportCommand`'da `Notify` parametresi yok.

- [ ] **Step 3: Komut ve işleyiciyi güncelle**

`ReportCommands.cs`:

```csharp
public sealed record RequestReportCommand(string ReportKey, Dictionary<string, string>? Parameters, bool Notify = true) : ICommand<ReportExecutionDto>;
```

ve işleyicide `if (currentUser.Email is { Length: > 0 } email)` satırını şu hale getir:

```csharp
            if (command.Notify && currentUser.Email is { Length: > 0 } email)
```

(`RequestReportCommand`'ın XML özetine bir cümle ekle: "`Notify=false` (the on-screen report page) skips the 'report ready' e-mail — the caller is already looking at the result.")

- [ ] **Step 4: Denetleyiciyi güncelle**

`ReportsController.cs`: istek kaydını ve `RequestReport` gövdesini değiştir,

```csharp
public sealed record RequestReportRequest(string ReportKey, Dictionary<string, string>? Parameters, bool? Notify = null);
```

```csharp
        FromResult(await Dispatcher.Send(new RequestReportCommand(request.ReportKey, request.Parameters, request.Notify ?? true), ct));
```

ve `Download` action'ının üstüne yeni ucu ekle:

```csharp
    /// <summary>A succeeded execution's workbook as grid data: sheets, columns and one searched/sorted/paged slice of rows.</summary>
    [HttpGet("executions/{id:guid}/data")]
    [ProducesResponseType<ReportDataDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Data(Guid id, [FromQuery] int sheet, [FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetReportExecutionDataQuery(id, sheet, paging), ct));
```

- [ ] **Step 5: Spec'i gerçekleşen sözleşmeyle hizala**

`docs/superpowers/specs/2026-09-28-genel-rapor-ekrani-design.md` içinde "**Uç:**" maddesindeki adresi ve yanıt cümlesini şöyle değiştir:
- Adres: `GET /v1/reports/executions/{id}/data?sheet=0&page=1&pageSize=50&q=&sort=` (`sort` sütun indeksi, azalan için `-` öneki: `sort=-2`; kod tabanının ortak `PagedQuery` kuralı).
- Yanıt: `sheets[] {index, name}`, `sheetIndex`, `columns[] {header, type}`, `rows` (ortak `PagedResult`: `items[][]`, `page`, `pageSize`, `totalCount`, `totalPages`, `hasNext`, `hasPrevious`).

- [ ] **Step 6: Derle ve testleri çalıştır**

Run: `dotnet test tests/Modules/Sense.Modules.Reporting.Tests/Sense.Modules.Reporting.Tests.csproj --filter "FullyQualifiedName!~Integration"`
Expected: PASS (Api projesi derlenir; mevcut testler bozulmaz).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Reporting/Sense.Modules.Reporting.Application/ReportCommands.cs src/Modules/Reporting/Sense.Modules.Reporting.Api/Controllers/ReportsController.cs docs/superpowers/specs/2026-09-28-genel-rapor-ekrani-design.md tests/Modules/Sense.Modules.Reporting.Tests/ReportingHandlerTests.cs
git commit -m "feat(reporting): notify bayragi ve rapor veri ucu"
```

---

### Task 5: Web servisi ve parametre yardımcıları

**Files:**
- Modify: `web/src/services/reports.service.ts`
- Create: `web/src/components/reports/report-parameters.ts`
- Test: `web/src/components/reports/report-parameters.test.ts`

**Interfaces:**
- Consumes: `REPORT_PARAMETER_KEYS`, `ReportParameter` (`reports.service.ts`), `toDateOnly` (`@/lib/dates`).
- Produces (`reports.service.ts`): `ReportColumnType`, `ReportColumn {header,type}`, `ReportSheet {index,name}`, `ReportCell`, `ReportData {sheets, sheetIndex, columns, rows: PagedResult<ReportCell[]>}`, `ReportDataParams {sheet?,page?,pageSize?,q?,sort?}`, `REPORT_PREVIEW_UNAVAILABLE_CODE`, `getReportExecutionData(id, params?)`, `requestReport(reportKey, parameters?, options?: { notify?: boolean })`.
  Produces (`report-parameters.ts`): `ParameterValues`, `defaultValuesFor(parameters, today)`, `isSatisfied(parameter, values)`, `allSatisfied(parameters, values)`, `valuesToSearchParams(values): URLSearchParams`, `valuesFromSearchParams(search: URLSearchParams, parameters, today): ParameterValues`.

- [ ] **Step 1: Başarısız testi yaz**

`web/src/components/reports/report-parameters.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import type { ReportParameter } from "@/services/reports.service";
import { allSatisfied, defaultValuesFor, valuesFromSearchParams, valuesToSearchParams } from "./report-parameters";

const TODAY = new Date(2026, 8, 15);

const YEAR: ReportParameter = { key: "year", kind: "year", isRequired: true };
const RANGE: ReportParameter = { key: "dateRange", kind: "dateRange", isRequired: true };
const UNITS: ReportParameter = { key: "orgUnitIds", kind: "orgUnits", isRequired: false };
const TERMINATED: ReportParameter = { key: "includeTerminated", kind: "boolean", isRequired: false };

describe("report-parameters", () => {
  it("defaults required year and date range to the current year and month", () => {
    expect(defaultValuesFor([YEAR, RANGE, UNITS], TODAY)).toEqual({ year: "2026", from: "2026-09-01", to: "2026-09-30" });
  });

  it("reports unsatisfied when a required filter is empty", () => {
    expect(allSatisfied([YEAR], {})).toBe(false);
    expect(allSatisfied([RANGE], { from: "2026-01-01" })).toBe(false);
    expect(allSatisfied([YEAR, UNITS], { year: "2026" })).toBe(true);
  });

  it("lets the address override defaults for the filters the report accepts", () => {
    const search = new URLSearchParams("year=2025&from=2025-01-01&to=2025-01-31&orgUnitIds=a,b&includeTerminated=true");
    expect(valuesFromSearchParams(search, [YEAR, RANGE, UNITS, TERMINATED], TODAY)).toEqual({
      year: "2025",
      from: "2025-01-01",
      to: "2025-01-31",
      orgUnitIds: "a,b",
      includeTerminated: "true",
    });
  });

  it("ignores address keys the report does not accept", () => {
    const search = new URLSearchParams("year=2025&orgUnitIds=a&evil=x");
    expect(valuesFromSearchParams(search, [YEAR], TODAY)).toEqual({ year: "2025" });
  });

  it("round-trips comma separated unit ids through the address", () => {
    const search = valuesToSearchParams({ orgUnitIds: "a,b", year: "2026" });
    expect(valuesFromSearchParams(new URLSearchParams(search.toString()), [YEAR, UNITS], TODAY)).toEqual({ year: "2026", orgUnitIds: "a,b" });
  });

  it("omits empty values from the address", () => {
    expect(valuesToSearchParams({ year: "", month: "3" }).toString()).toBe("month=3");
  });
});
```

- [ ] **Step 2: Başarısız olduğunu doğrula**

Run: `cd web && pnpm exec vitest run src/components/reports/report-parameters.test.ts`
Expected: FAIL — `./report-parameters` bulunamadı.

- [ ] **Step 3: Yardımcıları yaz**

`web/src/components/reports/report-parameters.ts` (mevcut `reports.tsx`'teki `defaultValuesFor` ve `isSatisfied` aynen taşınır):

```ts
import { toDateOnly } from "@/lib/dates";
import { REPORT_PARAMETER_KEYS, type ReportParameter } from "@/services/reports.service";

/** Filter values keyed by wire key (`year`, `from`, `to`, `orgUnitIds`, …); a `dateRange` parameter is stored as `from` + `to`. */
export type ParameterValues = Record<string, string>;

export function defaultValuesFor(parameters: ReportParameter[], today: Date): ParameterValues {
  const values: ParameterValues = {};
  for (const parameter of parameters) {
    if (!parameter.isRequired) continue;
    if (parameter.kind === "year") values[REPORT_PARAMETER_KEYS.year] = String(today.getFullYear());
    if (parameter.kind === "dateRange") {
      values[REPORT_PARAMETER_KEYS.from] = toDateOnly(new Date(today.getFullYear(), today.getMonth(), 1));
      values[REPORT_PARAMETER_KEYS.to] = toDateOnly(new Date(today.getFullYear(), today.getMonth() + 1, 0));
    }
  }
  return values;
}

export function isSatisfied(parameter: ReportParameter, values: ParameterValues): boolean {
  if (!parameter.isRequired) return true;
  if (parameter.kind === "dateRange") return Boolean(values[REPORT_PARAMETER_KEYS.from] && values[REPORT_PARAMETER_KEYS.to]);
  return Boolean(values[parameter.key]);
}

export function allSatisfied(parameters: ReportParameter[], values: ParameterValues): boolean {
  return parameters.every((parameter) => isSatisfied(parameter, values));
}

/** The wire keys a report's filters travel under (a date range is `from` + `to`). */
function wireKeys(parameters: ReportParameter[]): string[] {
  return parameters.flatMap((parameter) =>
    parameter.kind === "dateRange" ? [REPORT_PARAMETER_KEYS.from, REPORT_PARAMETER_KEYS.to] : [parameter.key],
  );
}

/** Filters → address query string (blank values omitted). Carries no personal data: unit ids, year/month, dates, a flag. */
export function valuesToSearchParams(values: ParameterValues): URLSearchParams {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) {
    if (value) search.set(key, value);
  }
  return search;
}

/** Address query string → filters: defaults first, then any value present for a filter this report accepts. */
export function valuesFromSearchParams(search: URLSearchParams, parameters: ReportParameter[], today: Date): ParameterValues {
  const values = defaultValuesFor(parameters, today);
  for (const key of wireKeys(parameters)) {
    const value = search.get(key);
    if (value) values[key] = value;
  }
  return values;
}
```

- [ ] **Step 4: Servisi genişlet**

`reports.service.ts`: `ReportExecution` arayüzünün altına ekle:

```ts
/** `ExportColumnType` (Shared.Contracts.Export), camelCase enum JSON. */
export type ReportColumnType = "text" | "number" | "date" | "dateTime" | "boolean";

export interface ReportColumn {
  header: string;
  type: ReportColumnType;
}

export interface ReportSheet {
  index: number;
  name: string;
}

/** Grid cell: text, number, boolean, ISO date ("yyyy-MM-dd") / date-time string, or null for a blank cell. */
export type ReportCell = string | number | boolean | null;

/** `ReportDataDto`: every sheet, the requested sheet's columns and one searched/sorted/paged slice of its rows. */
export interface ReportData {
  sheets: ReportSheet[];
  sheetIndex: number;
  columns: ReportColumn[];
  rows: PagedResult<ReportCell[]>;
}

export interface ReportDataParams {
  sheet?: number;
  page?: number;
  pageSize?: number;
  q?: string;
  /** Column index, `-` prefix for descending ("-2"). */
  sort?: string;
}

/** Problem `code` of `GET …/data` when the file cannot be shown as a grid (only downloaded). */
export const REPORT_PREVIEW_UNAVAILABLE_CODE = "report_execution.preview_not_available";
```

`requestReport`'ı değiştir:

```ts
/**
 * Requests a report; the response's status already reflects the (synchronous, R0) outcome.
 * `notify: false` (the on-screen report page) skips the "report ready" e-mail.
 */
export async function requestReport(
  reportKey: string,
  parameters?: Record<string, string>,
  options?: { notify?: boolean },
): Promise<ReportExecution> {
  const { data } = await apiClient.post<ReportExecution>(`${REPORTS_BASE}/executions`, { reportKey, parameters, notify: options?.notify });
  return data;
}
```

ve `getReportExecution`'ın altına ekle:

```ts
/** One page of a succeeded execution's workbook as grid data. */
export async function getReportExecutionData(id: string, params: ReportDataParams = {}): Promise<ReportData> {
  const { data } = await apiClient.get<ReportData>(`${REPORTS_BASE}/executions/${id}/data`, { params });
  return data;
}
```

- [ ] **Step 5: Test ve tip kontrolü**

Run: `cd web && pnpm exec vitest run src/components/reports/report-parameters.test.ts && pnpm exec tsc --noEmit`
Expected: PASS; tsc hatasız.

- [ ] **Step 6: Commit**

```bash
git add web/src/services/reports.service.ts web/src/components/reports/report-parameters.ts web/src/components/reports/report-parameters.test.ts
git commit -m "feat(web): rapor veri servisi ve filtre-adres yardimcilari"
```

---

### Task 6: Parametre formunu çıkar, panel "Raporu aç"a gitsin

**Files:**
- Create: `web/src/components/reports/report-parameter-form.tsx`
- Modify: `web/src/pages/reports.tsx`
- Modify: `web/public/locales/tr/reports.json`, `en/reports.json`, `de/reports.json`
- Test: `web/src/pages/reports.test.tsx`

**Interfaces:**
- Consumes: `ParameterValues`, `defaultValuesFor`, `allSatisfied`, `valuesToSearchParams` (Task 5); `useOrgUnits`; `DatePicker`.
- Produces: `ReportParameterForm({ report: ReportCatalogItem; values: ParameterValues; onChange: (values: ParameterValues) => void })`. Panelin düğmesi `/app/reports/<key>[?filtreler]` adresine gider.

- [ ] **Step 1: `frontend-standards` skill'ini çağır** (Skill aracı, `algosense-standards:frontend-standards`) ve mevcut sayfayla tutarlı kal; bu görev yeni görünüm tasarlamaz, mevcut formu taşır.

- [ ] **Step 2: Başarısız testi yaz**

`reports.test.tsx` importlarına `Route, Routes, useLocation` (`react-router`) ekle; `renderPage`'i router içeriğini rotalı hale getirerek değiştir:

```tsx
function ViewerStub() {
  const location = useLocation();
  return <div data-testid="viewer-location">{location.pathname + location.search}</div>;
}
```

`renderPage` içindeki `<MemoryRouter><ReportsPage /></MemoryRouter>` bloğunu şununla değiştir:

```tsx
          <MemoryRouter>
            <Routes>
              <Route path="/" element={<ReportsPage />} />
              <Route path="/app/reports/:key" element={<ViewerStub />} />
            </Routes>
          </MemoryRouter>
```

ve `describe` içine yeni test ekle:

```tsx
  it("opens the selected report on its own page from the run panel", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole("button", { name: /Çalışan listesi raporu/ }));
    await user.click(screen.getByRole("button", { name: "Raporu aç" }));

    expect(await screen.findByTestId("viewer-location")).toHaveTextContent("/app/reports/employee.list");
  });
```

- [ ] **Step 3: Başarısız olduğunu doğrula**

Run: `cd web && pnpm exec vitest run src/pages/reports.test.tsx`
Expected: FAIL — "Raporu aç" düğmesi yok.

- [ ] **Step 4: Formu çıkar**

`web/src/components/reports/report-parameter-form.tsx` (JSX, mevcut `ReportRunPanel` içindeki parametre bloğunun aynısıdır; durum yukarıdan `values`/`onChange` ile gelir):

```tsx
import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { Check } from "lucide-react";
import { Button, Group, Input, MultiSelect, Select, Stack, Switch, Text } from "@mantine/core";
import { DatePicker } from "@/components/ui/date-picker";
import { useOrgUnits } from "@/components/employees/use-org-units";
import { toDateOnly } from "@/lib/dates";
import { REPORT_PARAMETER_KEYS, type ReportCatalogItem } from "@/services/reports.service";
import type { ParameterValues } from "./report-parameters";

interface ReportParameterFormProps {
  report: ReportCatalogItem;
  values: ParameterValues;
  onChange: (values: ParameterValues) => void;
}

/** Kolay İK's "Birimler / Ay / Yıl / tarih aralığı" step, drawn from the report's parameter descriptors (controlled). */
export function ReportParameterForm({ report, values, onChange }: ReportParameterFormProps) {
  const { t, i18n } = useTranslation(["reports"]);
  const today = useMemo(() => new Date(), []);
  const { options: orgUnitOptions } = useOrgUnits();
  const hasRequired = report.parameters.some((p) => p.isRequired);

  const set = (key: string, value: string | null | undefined) => {
    const next = { ...values };
    if (value === null || value === undefined || value === "") delete next[key];
    else next[key] = value;
    onChange(next);
  };

  const monthOptions = useMemo(
    () =>
      Array.from({ length: 12 }, (_, i) => ({
        value: String(i + 1),
        label: new Intl.DateTimeFormat(i18n.language, { month: "long" }).format(new Date(2000, i, 1)),
      })),
    [i18n.language],
  );
  const yearOptions = useMemo(() => Array.from({ length: 7 }, (_, i) => String(today.getFullYear() + 1 - i)), [today]);
  const depthById = useMemo(() => new Map(orgUnitOptions.map((u) => [u.id, u.depth])), [orgUnitOptions]);

  const applyPreset = (preset: "thisMonth" | "lastMonth" | "thisYear") => {
    const y = today.getFullYear();
    const m = today.getMonth();
    const [from, to] =
      preset === "thisMonth"
        ? [new Date(y, m, 1), new Date(y, m + 1, 0)]
        : preset === "lastMonth"
          ? [new Date(y, m - 1, 1), new Date(y, m, 0)]
          : [new Date(y, 0, 1), new Date(y, 11, 31)];
    onChange({ ...values, [REPORT_PARAMETER_KEYS.from]: toDateOnly(from), [REPORT_PARAMETER_KEYS.to]: toDateOnly(to) });
  };

  if (report.parameters.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        {t("reports:run.noFilters")}
      </Text>
    );
  }

  return (
    <Stack gap="sm">
      {report.parameters.map((parameter) => {
        const required = parameter.isRequired ? " *" : "";
        switch (parameter.kind) {
          case "orgUnits":
            return (
              <MultiSelect
                key={parameter.key}
                label={t("reports:params.orgUnitIds") + required}
                description={t("reports:params.orgUnitIdsHint")}
                placeholder={t("reports:params.orgUnitIdsPlaceholder")}
                data={orgUnitOptions.map((u) => ({ value: u.id, label: u.name }))}
                value={values[parameter.key] ? values[parameter.key].split(",") : []}
                onChange={(ids) => set(parameter.key, ids.join(","))}
                searchable
                clearable
                renderOption={({ option, checked }) => (
                  <Group gap="xs" wrap="nowrap">
                    <Check size={14} style={{ opacity: checked ? 1 : 0, flexShrink: 0 }} />
                    <span style={{ paddingLeft: `${(depthById.get(option.value) ?? 0) * 12}px` }}>{option.label}</span>
                  </Group>
                )}
              />
            );
          case "year":
            return (
              <Select
                key={parameter.key}
                label={t("reports:params.year") + required}
                data={yearOptions}
                value={values[parameter.key] ?? null}
                onChange={(v) => set(parameter.key, v)}
                clearable={!parameter.isRequired}
              />
            );
          case "month":
            return (
              <Select
                key={parameter.key}
                label={t("reports:params.month") + required}
                placeholder={t("reports:params.monthPlaceholder")}
                data={monthOptions}
                value={values[parameter.key] ?? null}
                onChange={(v) => set(parameter.key, v)}
                clearable={!parameter.isRequired}
              />
            );
          case "dateRange":
            return (
              <Stack key={parameter.key} gap={6}>
                <Text size="sm" fw={500}>
                  {t("reports:params.dateRange") + required}
                </Text>
                <Group grow align="flex-end" gap="sm">
                  <Input.Wrapper id={`${report.key}-from`} label={t("reports:params.from")}>
                    <DatePicker
                      id={`${report.key}-from`}
                      value={values[REPORT_PARAMETER_KEYS.from] ?? ""}
                      max={values[REPORT_PARAMETER_KEYS.to] || undefined}
                      clearable={!parameter.isRequired}
                      onChange={(v) => set(REPORT_PARAMETER_KEYS.from, v)}
                    />
                  </Input.Wrapper>
                  <Input.Wrapper id={`${report.key}-to`} label={t("reports:params.to")}>
                    <DatePicker
                      id={`${report.key}-to`}
                      value={values[REPORT_PARAMETER_KEYS.to] ?? ""}
                      min={values[REPORT_PARAMETER_KEYS.from] || undefined}
                      clearable={!parameter.isRequired}
                      onChange={(v) => set(REPORT_PARAMETER_KEYS.to, v)}
                    />
                  </Input.Wrapper>
                </Group>
                <Group gap={6}>
                  {(["thisMonth", "lastMonth", "thisYear"] as const).map((preset) => (
                    <Button key={preset} type="button" size="compact-xs" variant="light" color="gray" onClick={() => applyPreset(preset)}>
                      {t(`reports:params.presets.${preset}`)}
                    </Button>
                  ))}
                </Group>
              </Stack>
            );
          case "boolean":
            return (
              <Switch
                key={parameter.key}
                label={t(`reports:params.${parameter.key}`, { defaultValue: parameter.key })}
                checked={values[parameter.key] === "true"}
                onChange={(e) => set(parameter.key, e.currentTarget.checked ? "true" : null)}
              />
            );
          default:
            return null;
        }
      })}
      {hasRequired && (
        <Text size="xs" c="dimmed">
          {t("reports:run.requiredHint")}
        </Text>
      )}
    </Stack>
  );
}
```

- [ ] **Step 5: `reports.tsx`'i güncelle**

1. Import bloğunu şu hale getir (kullanılmayanlar çıkar; ESLint `no-unused-vars` kalanları yakalar):

```tsx
import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery, type UseMutationResult } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { ArrowRight, BarChart3, Download, FileSpreadsheet } from "lucide-react";
import { Badge, Button, Card, Group, Skeleton, Stack, Tabs, Text, ThemeIcon, Title } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import PaginationFooter from "@/components/pagination-footer";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import { ReportCatalog, reportLabelKey } from "@/components/reports/report-catalog";
import { ReportParameterForm } from "@/components/reports/report-parameter-form";
import { allSatisfied, defaultValuesFor, valuesToSearchParams, type ParameterValues } from "@/components/reports/report-parameters";
import { WorkforceKpiPanel } from "@/components/reports/workforce-kpi-panel";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import {
  downloadReportExecution,
  getReportCatalog,
  getReportExecution,
  listReportExecutions,
  saveDownloadedReportFile,
  type DownloadedReportFile,
  type ReportCatalogItem,
  type ReportExecution,
  type ReportExecutionStatus,
} from "@/services/reports.service";
```

2. `ReportCatalogAndHistory` içinde `const queryClient = useQueryClient();` satırını sil; `<ReportRunPanel …>` kullanımını şuna çevir:

```tsx
              <ReportRunPanel key={selected.key} report={selected} canGenerate={canGenerate} />
```

3. `type ParameterValues = …` satırından `ReportRunPanel` fonksiyonunun sonuna kadar olan bloğu (yaklaşık satır 300–533; `defaultValuesFor`, `isSatisfied` ve eski panel dahil) sil ve yerine şunu koy:

```tsx
/** Kolay İK's "rapor türü → filtreler" step; the report itself opens on its own full page (`/app/reports/:key`). */
function ReportRunPanel({ report, canGenerate }: { report: ReportCatalogItem; canGenerate: boolean }) {
  const { t } = useTranslation(["reports"]);
  const navigate = useNavigate();
  const today = useMemo(() => new Date(), []);
  const [values, setValues] = useState<ParameterValues>(() => defaultValuesFor(report.parameters, today));

  const openReport = () => {
    const query = valuesToSearchParams(values).toString();
    void navigate(`/app/reports/${report.key}${query ? `?${query}` : ""}`);
  };

  return (
    <Card withBorder padding="lg" radius="md">
      <Stack gap="md">
        <div>
          <Text size="xs" c="dimmed" tt="uppercase" fw={600} lts={0.5}>
            {t("reports:run.title")}
          </Text>
          <Text fw={700} size="md" mt={2}>
            {t(reportLabelKey(report.key, "label"), { defaultValue: report.key })}
          </Text>
          <Text size="xs" c="dimmed" mt={4}>
            {t(reportLabelKey(report.key, "description"), { defaultValue: "" })}
          </Text>
        </div>

        <ReportParameterForm report={report} values={values} onChange={setValues} />

        {canGenerate && (
          <Group justify="flex-end">
            <Button type="button" rightSection={<ArrowRight size={16} />} disabled={!allSatisfied(report.parameters, values)} onClick={openReport}>
              {t("reports:run.open")}
            </Button>
          </Group>
        )}
      </Stack>
    </Card>
  );
}
```

- [ ] **Step 6: Çevirileri güncelle**

Üç `reports.json` dosyasında `run` nesnesine `"open"` ekle; artık kullanılmayan `generate`, `success`, `successNoDownload`, `emailNote`, `format` anahtarlarını sil (önce `grep -rn "reports:run\.\(generate\|success\|successNoDownload\|emailNote\|format\)" web/src` ile başka kullanım olmadığını doğrula; `run.error`, `run.title`, `run.requiredHint`, `run.noFilters` kalır):
- tr: `"open": "Raporu aç"`
- en: `"open": "Open report"`
- de: `"open": "Bericht öffnen"`

- [ ] **Step 7: Doğrula**

Run: `cd web && pnpm exec vitest run src/pages/reports.test.tsx && pnpm exec tsc --noEmit && pnpm exec eslint src/pages/reports.tsx src/components/reports`
Expected: hepsi PASS/temiz (5 test).

- [ ] **Step 8: Commit**

```bash
git add web/src/components/reports/report-parameter-form.tsx web/src/pages/reports.tsx web/src/pages/reports.test.tsx web/public/locales/tr/reports.json web/public/locales/en/reports.json web/public/locales/de/reports.json
git commit -m "refactor(web): rapor filtre formu ortak bilesen; panel raporu tam sayfada acar"
```

---

### Task 7: `ReportDataGrid` bileşeni

**Files:**
- Create: `web/src/components/reports/report-data-grid.tsx`
- Modify: `web/public/locales/{tr,en,de}/reports.json`
- Test: `web/src/components/reports/report-data-grid.test.tsx`

**Interfaces:**
- Consumes: `ReportData`, `ReportCell`, `ReportColumnType` (Task 5); `DataTable`, `useDataTable` (manuel sıralama için `sorting` + `onSortingChange`); `PaginationFooter`; `formatDateOnly`, `formatDateTime`.
- Produces: `ReportDataGrid(props: ReportDataGridProps)`:

```ts
interface ReportDataGridProps {
  data: ReportData | undefined;
  isLoading: boolean;
  isFetching: boolean;
  search: string;
  onSearchChange: (value: string) => void;
  sorting: SortingState;                       // sütun kimliği = sütun indeksi ("0", "1", …)
  onSortingChange: (sorting: SortingState) => void;
  onSheetChange: (index: number) => void;
  onPageChange: (page: number) => void;
  pageSize: number;
  onPageSizeChange: (size: number) => void;
}
```

- [ ] **Step 1: `frontend-standards` skill'ini çağır**; gridin görünümü mevcut `DataTable` (Mantine tablo, sticky başlık) ve katalog aramasıyla aynı dilde kalır.

- [ ] **Step 2: Başarısız testleri yaz**

`web/src/components/reports/report-data-grid.test.tsx`:

```tsx
import { beforeAll, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import trReports from "../../../public/locales/tr/reports.json";
import trCommon from "../../../public/locales/tr/common.json";
import type { ReportData } from "@/services/reports.service";
import { ReportDataGrid } from "./report-data-grid";

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

function pagedRows(items: ReportData["rows"]["items"], totalCount = items.length): ReportData["rows"] {
  return { items, page: 1, pageSize: 50, totalCount, totalPages: Math.max(1, Math.ceil(totalCount / 50)), hasNext: false, hasPrevious: false };
}

const DATA: ReportData = {
  sheets: [{ index: 0, name: "Kişiler" }],
  sheetIndex: 0,
  columns: [
    { header: "Ad", type: "text" },
    { header: "Maaş", type: "number" },
    { header: "İşe giriş", type: "date" },
    { header: "Aktif", type: "boolean" },
  ],
  rows: pagedRows([
    ["Ayşe", 45000.5, "2024-03-01", true],
    ["Mehmet", null, "2023-11-15", false],
  ]),
};

function renderGrid(overrides: Partial<React.ComponentProps<typeof ReportDataGrid>> = {}) {
  const props: React.ComponentProps<typeof ReportDataGrid> = {
    data: DATA,
    isLoading: false,
    isFetching: false,
    search: "",
    onSearchChange: vi.fn(),
    sorting: [],
    onSortingChange: vi.fn(),
    onSheetChange: vi.fn(),
    onPageChange: vi.fn(),
    pageSize: 50,
    onPageSizeChange: vi.fn(),
    ...overrides,
  };
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "reports",
    ns: ["reports", "common"],
    resources: { tr: { reports: trReports, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  render(
    <I18nextProvider i18n={instance}>
      <MantineProvider>
        <ReportDataGrid {...props} />
      </MantineProvider>
    </I18nextProvider>,
  );
  return props;
}

describe("ReportDataGrid", () => {
  it("renders headers and formats numbers, dates and booleans", () => {
    renderGrid();

    expect(screen.getByRole("columnheader", { name: /Maaş/ })).toBeInTheDocument();
    expect(screen.getByText("Ayşe")).toBeInTheDocument();
    expect(screen.getByText("45.000,5")).toBeInTheDocument();
    expect(screen.getByText(/2024/)).toBeInTheDocument();
    expect(screen.getByText("Evet")).toBeInTheDocument();
    expect(screen.getByText("Hayır")).toBeInTheDocument();
  });

  it("reports a sort request by column index when a header is clicked", async () => {
    const user = userEvent.setup();
    const props = renderGrid();

    await user.click(screen.getByRole("button", { name: /Maaş/ }));

    expect(props.onSortingChange).toHaveBeenCalledWith([{ id: "1", desc: false }]);
  });

  it("reports typed search text", async () => {
    const user = userEvent.setup();
    const props = renderGrid();

    await user.type(screen.getByPlaceholderText("Sonuçlarda ara"), "a");

    expect(props.onSearchChange).toHaveBeenCalledWith("a");
  });

  it("shows sheet tabs only when there are several sheets and reports the chosen one", async () => {
    const user = userEvent.setup();
    const props = renderGrid({ data: { ...DATA, sheets: [{ index: 0, name: "Kişiler" }, { index: 1, name: "Özet" }] } });

    await user.click(screen.getByRole("tab", { name: "Özet" }));

    expect(props.onSheetChange).toHaveBeenCalledWith(1);
  });

  it("does not show sheet tabs for a single sheet", () => {
    renderGrid();

    expect(screen.queryByRole("tab")).not.toBeInTheDocument();
  });

  it("says there are no records and hides the pager for an empty report", () => {
    renderGrid({ data: { ...DATA, rows: pagedRows([]) } });

    expect(screen.getByText("Bu filtrelerle gösterilecek kayıt yok.")).toBeInTheDocument();
    expect(screen.queryByRole("navigation")).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Başarısız olduğunu doğrula**

Run: `cd web && pnpm exec vitest run src/components/reports/report-data-grid.test.tsx`
Expected: FAIL — `./report-data-grid` bulunamadı.

- [ ] **Step 4: Bileşeni yaz**

`web/src/components/reports/report-data-grid.tsx`:

```tsx
import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import { Card, Stack, Tabs, TextInput } from "@mantine/core";
import { Search } from "lucide-react";
import PaginationFooter from "@/components/pagination-footer";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { formatDateOnly, formatDateTime } from "@/lib/dates";
import type { ReportCell, ReportColumnType, ReportData } from "@/services/reports.service";

type Row = ReportCell[];

interface ReportDataGridProps {
  data: ReportData | undefined;
  isLoading: boolean;
  isFetching: boolean;
  search: string;
  onSearchChange: (value: string) => void;
  /** Column id is the column index ("0", "1", …); the server sorts by it. */
  sorting: SortingState;
  onSortingChange: (sorting: SortingState) => void;
  onSheetChange: (index: number) => void;
  onPageChange: (page: number) => void;
  pageSize: number;
  onPageSizeChange: (size: number) => void;
}

function formatCell(value: ReportCell, type: ReportColumnType, locale: string, yes: string, no: string): string {
  if (value === null || value === undefined) return "";
  switch (type) {
    case "number":
      return typeof value === "number" ? new Intl.NumberFormat(locale).format(value) : String(value);
    case "date":
      return formatDateOnly(String(value), locale);
    case "dateTime":
      return formatDateTime(String(value), locale);
    case "boolean":
      return value === true ? yes : value === false ? no : String(value);
    default:
      return String(value);
  }
}

/** Generic report grid: sheet tabs, search, server-sorted/paged table whose columns come from the workbook's header row. */
export function ReportDataGrid({
  data,
  isLoading,
  isFetching,
  search,
  onSearchChange,
  sorting,
  onSortingChange,
  onSheetChange,
  onPageChange,
  pageSize,
  onPageSizeChange,
}: ReportDataGridProps) {
  const { t, i18n } = useTranslation(["reports", "common"]);
  const locale = i18n.language === "tr" ? "tr-TR" : i18n.language === "de" ? "de-DE" : "en-US";
  const yes = t("common:yes");
  const no = t("common:no");

  const columns = useMemo<ColumnDef<Row>[]>(
    () =>
      (data?.columns ?? []).map((column, index) => ({
        id: String(index),
        header: column.header || `#${index + 1}`,
        accessorFn: (row) => row[index] ?? null,
        meta: {
          label: column.header,
          headerClassName: column.type === "number" ? "text-right" : undefined,
          cellClassName: column.type === "number" ? "text-right tabular-nums" : undefined,
        },
        cell: ({ getValue }) => formatCell(getValue() as ReportCell, column.type, locale, yes, no),
      })),
    [data?.columns, locale, yes, no],
  );

  const table = useDataTable({
    columns,
    data: data?.rows.items ?? [],
    sorting,
    onSortingChange,
  });

  return (
    <Stack gap="sm">
      {data && data.sheets.length > 1 && (
        <Tabs value={String(data.sheetIndex)} onChange={(value) => value !== null && onSheetChange(Number(value))}>
          <Tabs.List>
            {data.sheets.map((sheet) => (
              <Tabs.Tab key={sheet.index} value={String(sheet.index)}>
                {sheet.name}
              </Tabs.Tab>
            ))}
          </Tabs.List>
        </Tabs>
      )}

      <TextInput
        aria-label={t("reports:viewer.search")}
        placeholder={t("reports:viewer.search")}
        leftSection={<Search size={14} />}
        value={search}
        onChange={(event) => onSearchChange(event.currentTarget.value)}
        maw={360}
      />

      <Card withBorder padding={0} radius="md">
        <DataTable table={table} isLoading={isLoading} isFetching={isFetching} emptyMessage={t("reports:viewer.noRows")} stickyHeader />
      </Card>

      {data && <PaginationFooter result={data.rows} onPageChange={onPageChange} pageSize={pageSize} onPageSizeChange={onPageSizeChange} />}
    </Stack>
  );
}
```

- [ ] **Step 5: Çevirileri ekle**

Üç `reports.json` dosyasına `viewer` nesnesi ekle (`run`'dan sonra; Task 8 aynı nesneye kalan anahtarları ekler — bu görevde yalnız aşağıdaki iki anahtar):
- tr: `"viewer": { "search": "Sonuçlarda ara", "noRows": "Bu filtrelerle gösterilecek kayıt yok." }`
- en: `"viewer": { "search": "Search results", "noRows": "No records match these filters." }`
- de: `"viewer": { "search": "In den Ergebnissen suchen", "noRows": "Keine Datensätze für diese Filter." }`

- [ ] **Step 6: Doğrula**

Run: `cd web && pnpm exec vitest run src/components/reports/report-data-grid.test.tsx && pnpm exec tsc --noEmit && pnpm exec eslint src/components/reports`
Expected: PASS (6 test), tsc/eslint temiz. `getByText("45.000,5")` yerel ICU'ya bağlı olarak düşerse `tr-TR` sayı biçimini `Intl.NumberFormat("tr-TR").format(45000.5)` çıktısıyla karşılaştır (kod doğru, beklenti ortamdan etkilenmiş demektir).

- [ ] **Step 7: Commit**

```bash
git add web/src/components/reports/report-data-grid.tsx web/src/components/reports/report-data-grid.test.tsx web/public/locales/tr/reports.json web/public/locales/en/reports.json web/public/locales/de/reports.json
git commit -m "feat(web): genel rapor grid bileseni"
```

---

### Task 8: `report-viewer` sayfası ve rota

**Files:**
- Create: `web/src/pages/report-viewer.tsx`
- Modify: `web/src/App.tsx` (lazy import + rota)
- Modify: `web/public/locales/{tr,en,de}/reports.json`
- Test: `web/src/pages/report-viewer.test.tsx`

**Interfaces:**
- Consumes: Task 5–7'nin tümü; `getReportCatalog`, `getReportExecution`, `getReportExecutionData`, `requestReport`, `downloadReportExecution`, `saveDownloadedReportFile`, `REPORT_PREVIEW_UNAVAILABLE_CODE`; `getApiProblem`, `getApiErrorMessage`; `usePermission`; `NoAccess`.
- Produces: default export `ReportViewerPage` (rota `reports/:key`, adres `?<filtreler>&execution=<id>`).

- [ ] **Step 1: `frontend-standards` skill'ini çağır**; sayfa tam genişlikte, üstte geri bağlantısı + başlık + filtre kartı, altta grid. Mevcut `reports.tsx` başlık/kart diliyle tutarlı kal.

- [ ] **Step 2: Başarısız testleri yaz**

`web/src/pages/report-viewer.test.tsx`:

```tsx
import { StrictMode } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, type AxiosResponse } from "axios";
import { I18nextProvider } from "react-i18next";
import { MemoryRouter, Route, Routes } from "react-router";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trReports from "../../public/locales/tr/reports.json";
import trCommon from "../../public/locales/tr/common.json";
import ReportViewerPage from "./report-viewer";
import {
  downloadReportExecution,
  getReportCatalog,
  getReportExecution,
  getReportExecutionData,
  requestReport,
  saveDownloadedReportFile,
  type ReportCatalogItem,
  type ReportData,
  type ReportExecution,
} from "@/services/reports.service";

vi.mock("@/services/reports.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/reports.service")>("@/services/reports.service");
  return {
    ...actual,
    getReportCatalog: vi.fn(),
    getReportExecution: vi.fn(),
    getReportExecutionData: vi.fn(),
    requestReport: vi.fn(),
    downloadReportExecution: vi.fn(),
    saveDownloadedReportFile: vi.fn(),
  };
});

vi.mock("@/components/permission-guard", () => ({
  usePermission: () => true,
  useAnyPermission: () => true,
}));

vi.mock("@/components/employees/use-org-units", () => ({
  useOrgUnits: () => ({ options: [], nameById: new Map(), isLoading: false }),
}));

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

const NO_FILTER: ReportCatalogItem = { key: "employee.list", categoryKey: "employee", parameters: [] };
const WITH_YEAR: ReportCatalogItem = {
  key: "employee.departed",
  categoryKey: "employee",
  parameters: [{ key: "year", kind: "year", isRequired: true }],
};

const EXECUTION: ReportExecution = {
  id: "exec-1",
  reportKey: "employee.list",
  status: "succeeded",
  requestedAt: "2026-09-28T10:00:00Z",
  resultFileName: "Çalışan listesi.xlsx",
};

const DATA: ReportData = {
  sheets: [{ index: 0, name: "Çalışanlar" }],
  sheetIndex: 0,
  columns: [{ header: "Ad", type: "text" }],
  rows: { items: [["Ayşe"], ["Mehmet"]], page: 1, pageSize: 50, totalCount: 2, totalPages: 1, hasNext: false, hasPrevious: false },
};

function renderPage(entry: string, { strict = false } = {}) {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "reports",
    ns: ["reports", "common"],
    resources: { tr: { reports: trReports, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const tree = (
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter initialEntries={[entry]}>
            <Routes>
              <Route path="/app/reports/:key" element={<ReportViewerPage />} />
            </Routes>
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
  return render(strict ? <StrictMode>{tree}</StrictMode> : tree);
}

describe("ReportViewerPage", () => {
  beforeEach(() => {
    vi.mocked(getReportCatalog).mockResolvedValue([NO_FILTER, WITH_YEAR]);
    vi.mocked(requestReport).mockResolvedValue(EXECUTION);
    vi.mocked(getReportExecution).mockResolvedValue(EXECUTION);
    vi.mocked(getReportExecutionData).mockResolvedValue(DATA);
    vi.mocked(downloadReportExecution).mockResolvedValue({ blob: new Blob(), fileName: "x.xlsx" });
  });

  it("generates the report once on open (even under StrictMode) without the e-mail and shows the rows", async () => {
    renderPage("/app/reports/employee.list", { strict: true });

    expect(await screen.findByText("Ayşe")).toBeInTheDocument();
    expect(screen.getByText("Mehmet")).toBeInTheDocument();
    expect(requestReport).toHaveBeenCalledTimes(1);
    expect(requestReport).toHaveBeenCalledWith("employee.list", {}, { notify: false });
  });

  it("carries the filters from the address into the request", async () => {
    vi.mocked(requestReport).mockResolvedValue({ ...EXECUTION, reportKey: "employee.departed" });

    renderPage("/app/reports/employee.departed?year=2025");

    await waitFor(() => expect(requestReport).toHaveBeenCalledWith("employee.departed", { year: "2025" }, { notify: false }));
  });

  it("restores a report from ?execution= without generating again", async () => {
    renderPage("/app/reports/employee.list?execution=exec-1");

    expect(await screen.findByText("Ayşe")).toBeInTheDocument();
    expect(getReportExecution).toHaveBeenCalledWith("exec-1");
    expect(requestReport).not.toHaveBeenCalled();
  });

  it("drops a stale or foreign ?execution= and generates the report instead", async () => {
    vi.mocked(getReportExecution).mockRejectedValue(new Error("gone"));

    renderPage("/app/reports/employee.list?execution=someone-elses");

    expect(await screen.findByText("Ayşe")).toBeInTheDocument();
    expect(requestReport).toHaveBeenCalledTimes(1);
  });

  it("shows the execution's error message when generation failed", async () => {
    vi.mocked(requestReport).mockResolvedValue({ ...EXECUTION, status: "failed", errorMessage: "boom" });

    renderPage("/app/reports/employee.list");

    expect(await screen.findByText("boom")).toBeInTheDocument();
    expect(screen.queryByText("Ayşe")).not.toBeInTheDocument();
  });

  it("downloads the same execution's Excel file from the button", async () => {
    const user = userEvent.setup();
    renderPage("/app/reports/employee.list?execution=exec-1");
    await screen.findByText("Ayşe");

    await user.click(screen.getByRole("button", { name: "Excel indir" }));

    await waitFor(() => expect(downloadReportExecution).toHaveBeenCalledWith("exec-1", "Çalışan listesi.xlsx"));
    await waitFor(() => expect(saveDownloadedReportFile).toHaveBeenCalled());
    expect(requestReport).not.toHaveBeenCalled();
  });

  it("offers only the download when the file cannot be previewed", async () => {
    const response = { status: 422, data: { code: "report_execution.preview_not_available" } } as AxiosResponse;
    vi.mocked(getReportExecutionData).mockRejectedValue(new AxiosError("bad", "ERR_BAD_REQUEST", undefined, undefined, response));

    renderPage("/app/reports/employee.list?execution=exec-1");

    expect(await screen.findByText("Bu rapor ekranda gösterilemiyor; Excel olarak indirebilirsiniz.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Excel indir" })).toBeEnabled();
  });

  it("says so when the report is not in the catalog", async () => {
    renderPage("/app/reports/unknown.report");

    expect(await screen.findByText("Bu rapor bulunamadı ya da erişiminiz yok.")).toBeInTheDocument();
    expect(requestReport).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 3: Başarısız olduğunu doğrula**

Run: `cd web && pnpm exec vitest run src/pages/report-viewer.test.tsx`
Expected: FAIL — `./report-viewer` bulunamadı.

- [ ] **Step 4: Sayfayı yaz**

`web/src/pages/report-viewer.tsx`:

```tsx
import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { SortingState } from "@tanstack/react-table";
import { ArrowLeft, Download, Play } from "lucide-react";
import { Alert, Button, Card, Group, Skeleton, Stack, Text, Title } from "@mantine/core";
import { useDebouncedValue } from "@mantine/hooks";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { reportLabelKey } from "@/components/reports/report-catalog";
import { ReportDataGrid } from "@/components/reports/report-data-grid";
import { ReportParameterForm } from "@/components/reports/report-parameter-form";
import { allSatisfied, valuesFromSearchParams, valuesToSearchParams, type ParameterValues } from "@/components/reports/report-parameters";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import {
  downloadReportExecution,
  getReportCatalog,
  getReportExecution,
  getReportExecutionData,
  REPORT_PREVIEW_UNAVAILABLE_CODE,
  requestReport,
  saveDownloadedReportFile,
  type ReportCatalogItem,
} from "@/services/reports.service";

const DEFAULT_PAGE_SIZE = 50;
const SEARCH_DEBOUNCE_MS = 300;
const EXECUTION_PARAM = "execution";

/**
 * Generic full-page report (roadmap #70): opens from the catalog with the chosen filters in the address, generates the
 * report once (no e-mail - the user is looking at it), shows the workbook as a grid and downloads that same execution's
 * Excel file. `?execution=<id>` pins the result so a refresh or a shared link restores it without generating again.
 */
export default function ReportViewerPage() {
  const { t } = useTranslation(["reports"]);
  const { key = "" } = useParams();
  const canViewCatalog = usePermission("reports.catalog.view");
  const canViewHistory = usePermission("reports.my.executions");
  const { data: catalog, isLoading } = useQuery({ queryKey: ["reports", "catalog"], queryFn: getReportCatalog, enabled: canViewCatalog });

  if (!canViewCatalog || !canViewHistory) return <NoAccess />;

  const report = catalog?.find((item) => item.key === key);

  return (
    <div className="space-y-4 p-4 md:p-6">
      <Button component={Link} to="/app/reports" variant="subtle" size="compact-sm" leftSection={<ArrowLeft size={16} />}>
        {t("reports:viewer.back")}
      </Button>
      {isLoading ? (
        <Skeleton h={160} />
      ) : !report ? (
        <Alert color="red" variant="light">
          {t("reports:viewer.notFound")}
        </Alert>
      ) : (
        <ReportViewer key={report.key} report={report} />
      )}
    </div>
  );
}

function ReportViewer({ report }: { report: ReportCatalogItem }) {
  const { t } = useTranslation(["reports", "common"]);
  const canGenerate = usePermission("reports.executions.generate");
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const today = useMemo(() => new Date(), []);
  const [values, setValues] = useState<ParameterValues>(() => valuesFromSearchParams(searchParams, report.parameters, today));
  const executionId = searchParams.get(EXECUTION_PARAM);
  const autoRunStarted = useRef(false);
  const [runError, setRunError] = useState<string | null>(null);

  const [sheet, setSheet] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [searchInput, setSearchInput] = useState("");
  const [search] = useDebouncedValue(searchInput.trim(), SEARCH_DEBOUNCE_MS);
  const [sorting, setSorting] = useState<SortingState>([]);
  const sort = sorting[0] ? `${sorting[0].desc ? "-" : ""}${sorting[0].id}` : undefined;

  const canRun = canGenerate && allSatisfied(report.parameters, values);

  const runMutation = useMutation({
    mutationFn: () => requestReport(report.key, values, { notify: false }),
    onSuccess: (execution) => {
      void queryClient.invalidateQueries({ queryKey: ["reports", "executions"] });
      if (execution.status === "failed") {
        setRunError(execution.errorMessage ?? t("reports:viewer.runError"));
        return;
      }
      queryClient.setQueryData(["reports", "execution", execution.id], execution);
      setSheet(0);
      setPage(1);
      setSorting([]);
      setSearchInput("");
      const next = valuesToSearchParams(values);
      next.set(EXECUTION_PARAM, execution.id);
      setSearchParams(next, { replace: true });
    },
    onError: (error) => setRunError(getApiErrorMessage(error, t("reports:viewer.runError"))),
  });

  // Open = run: the report is generated once as soon as the filters are complete and no execution is pinned in the address.
  useEffect(() => {
    if (executionId || autoRunStarted.current || !canRun) return;
    autoRunStarted.current = true;
    runMutation.mutate();
  }, [executionId, canRun, runMutation]);

  const executionQuery = useQuery({
    queryKey: ["reports", "execution", executionId],
    queryFn: () => getReportExecution(executionId ?? ""),
    enabled: executionId !== null,
    retry: false,
  });
  const execution = executionQuery.data && executionQuery.data.reportKey === report.key ? executionQuery.data : null;

  // A pinned execution that is gone (retention), foreign or of another report is dropped so the open-effect can run again.
  const executionUnusable = executionId !== null && (executionQuery.isError || (executionQuery.isSuccess && execution === null));
  useEffect(() => {
    if (!executionUnusable) return;
    const next = new URLSearchParams(searchParams);
    next.delete(EXECUTION_PARAM);
    setSearchParams(next, { replace: true });
  }, [executionUnusable, searchParams, setSearchParams]);

  const succeeded = execution?.status === "succeeded";
  const dataQuery = useQuery({
    queryKey: ["reports", "execution-data", execution?.id, sheet, page, pageSize, search, sort],
    queryFn: () => getReportExecutionData(execution?.id ?? "", { sheet, page, pageSize, q: search || undefined, sort }),
    enabled: succeeded,
    placeholderData: (previous) => previous,
    retry: false,
  });
  const previewUnavailable = getApiProblem(dataQuery.error)?.code === REPORT_PREVIEW_UNAVAILABLE_CODE;

  const downloadMutation = useMutation({
    mutationFn: () => downloadReportExecution(execution?.id ?? "", execution?.resultFileName ?? `${report.key}.xlsx`),
    onSuccess: saveDownloadedReportFile,
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("reports:history.downloadError")) }),
  });

  const apply = () => {
    setRunError(null);
    runMutation.mutate();
  };

  const failedMessage = runError ?? (execution?.status === "failed" ? (execution.errorMessage ?? t("reports:viewer.runError")) : null);
  const showIdleHint = !executionId && !runMutation.isPending && !failedMessage && !canRun;

  return (
    <Stack gap="md">
      <div>
        <Title order={1} size="h3">
          {t(reportLabelKey(report.key, "label"), { defaultValue: report.key })}
        </Title>
        <Text c="dimmed" size="sm" mt={4}>
          {t(reportLabelKey(report.key, "description"), { defaultValue: "" })}
        </Text>
      </div>

      <Card withBorder padding="md" radius="md">
        <Stack gap="md">
          <ReportParameterForm report={report} values={values} onChange={setValues} />
          <Group justify="flex-end" gap="sm">
            <Button
              type="button"
              variant="default"
              leftSection={<Download size={16} />}
              disabled={!succeeded}
              loading={downloadMutation.isPending}
              onClick={() => downloadMutation.mutate()}
            >
              {t("reports:viewer.downloadExcel")}
            </Button>
            {canGenerate && (
              <Button type="button" leftSection={<Play size={16} />} disabled={!canRun} loading={runMutation.isPending} onClick={apply}>
                {t("reports:viewer.apply")}
              </Button>
            )}
          </Group>
        </Stack>
      </Card>

      {failedMessage && (
        <Alert color="red" variant="light">
          {failedMessage}
        </Alert>
      )}
      {previewUnavailable && (
        <Alert color="gray" variant="light">
          {t("reports:viewer.previewUnavailable")}
        </Alert>
      )}
      {showIdleHint && (
        <Text size="sm" c="dimmed">
          {t("reports:viewer.idle")}
        </Text>
      )}

      {runMutation.isPending ? (
        <Skeleton h={240} radius="md" />
      ) : (
        succeeded &&
        !previewUnavailable && (
          <ReportDataGrid
            data={dataQuery.data}
            isLoading={dataQuery.isLoading}
            isFetching={dataQuery.isFetching}
            search={searchInput}
            onSearchChange={(value) => {
              setSearchInput(value);
              setPage(1);
            }}
            sorting={sorting}
            onSortingChange={(next) => {
              setSorting(next.slice(-1));
              setPage(1);
            }}
            onSheetChange={(index) => {
              setSheet(index);
              setPage(1);
              setSorting([]);
              setSearchInput("");
            }}
            onPageChange={setPage}
            pageSize={pageSize}
            onPageSizeChange={(size) => {
              setPageSize(size);
              setPage(1);
            }}
          />
        )
      )}
    </Stack>
  );
}
```

- [ ] **Step 5: Rotayı ekle**

`web/src/App.tsx`: `const ReportsPage = lazy(() => import("@/pages/reports"));` satırının altına ekle:

```tsx
const ReportViewerPage = lazy(() => import("@/pages/report-viewer"));
```

ve `<Route path="reports" element={<ReportsPage />} />` satırının altına ekle:

```tsx
            <Route path="reports/:key" element={<ReportViewerPage />} />
```

- [ ] **Step 6: Çevirileri tamamla**

Üç `reports.json`'da Task 7'de açılan `viewer` nesnesine kalan anahtarları ekle:
- tr: `"back": "Raporlara dön"`, `"apply": "Uygula"`, `"downloadExcel": "Excel indir"`, `"notFound": "Bu rapor bulunamadı ya da erişiminiz yok."`, `"previewUnavailable": "Bu rapor ekranda gösterilemiyor; Excel olarak indirebilirsiniz."`, `"runError": "Rapor oluşturulamadı."`, `"idle": "Zorunlu filtreleri seçip Uygula'ya basın."`
- en: `"back": "Back to reports"`, `"apply": "Apply"`, `"downloadExcel": "Download Excel"`, `"notFound": "This report was not found or you do not have access."`, `"previewUnavailable": "This report cannot be shown on screen; you can download it as Excel."`, `"runError": "The report could not be generated."`, `"idle": "Choose the required filters and press Apply."`
- de: `"back": "Zurück zu den Berichten"`, `"apply": "Anwenden"`, `"downloadExcel": "Excel herunterladen"`, `"notFound": "Dieser Bericht wurde nicht gefunden oder Sie haben keinen Zugriff."`, `"previewUnavailable": "Dieser Bericht kann nicht am Bildschirm angezeigt werden; Sie können ihn als Excel herunterladen."`, `"runError": "Der Bericht konnte nicht erstellt werden."`, `"idle": "Wählen Sie die erforderlichen Filter und klicken Sie auf Anwenden."`

- [ ] **Step 7: Doğrula**

Run: `cd web && pnpm exec vitest run src/pages/report-viewer.test.tsx src/pages/reports.test.tsx && pnpm exec tsc --noEmit && pnpm exec eslint src`
Expected: PASS (8 + 5 test), tsc/eslint temiz. StrictMode testi çift üretimle düşerse: `autoRunStarted` ref korumasını ve efekt bağımlılıklarını kontrol et — koruma kaldırılmaz.

- [ ] **Step 8: Commit**

```bash
git add web/src/pages/report-viewer.tsx web/src/pages/report-viewer.test.tsx web/src/App.tsx web/public/locales/tr/reports.json web/public/locales/en/reports.json web/public/locales/de/reports.json
git commit -m "feat(web): rapor tam sayfa gorunumu ve Excel indir"
```

---

### Task 9: Doğrulama, belgeler ve inceleme

**Files:**
- Modify: `docs/analysis/kolayik-video/faz1a-durum-takibi.md` (Raporlar / #70 maddesi)

- [ ] **Step 1: Tam derleme ve testler**

Run:
```bash
dotnet test tests/Modules/Sense.Modules.Reporting.Tests/Sense.Modules.Reporting.Tests.csproj --filter "FullyQualifiedName!~Integration"
dotnet test tests/Modules/Sense.Shared.Infrastructure.Tests/Sense.Shared.Infrastructure.Tests.csproj
cd web && pnpm exec tsc --noEmit && pnpm exec eslint src && pnpm exec vitest run src/pages src/components/reports && pnpm exec vite build
```
Expected: hepsi PASS/temiz. Sağlayıcı testleri etkilenmemeli; ayrıca `dotnet build src/Sense.Api` (ya da solution'da API projesi) derlenir.

- [ ] **Step 2: Test kapısı**

Run (worktree kökünde): `node .claude/takim/betikler/test-kapisi.mjs`
Expected: durum `yesil`. `kirmizi`/`taban-alti` ise nedeni düzelt; testleri silme/atlama yok.

- [ ] **Step 3: Durum takibi**

`grep -n "#70\|Raporlar" docs/analysis/kolayik-video/faz1a-durum-takibi.md` ile raporlar maddesini bul; o maddenin altına koda göre doğrulanmış tek satır ekle: "Rapor ekranı: rapor `/app/reports/:key` tam sayfasında grid olarak açılır (arama/sıralama/sayfalama, çok sayfalı çalışma kitabı), Excel aynı sayfadan indirilir — `GET /v1/reports/executions/{id}/data`, `IReportTableReader`; sağlayıcılar değişmedi." Maddenin bulunamadığı durumda dosyada Raporlama ile ilgili bölüm yoksa bu adım atlanır ve kullanıcıya söylenir.

- [ ] **Step 4: Bağımsız inceleme**

`takim-gozden-gecirici` ajanını çağır (kabul kriterleri = spec §1–§4 ve bu planın Review Focus'u); ajan test kapısını kendisi yeniden koşturur. CRITICAL/HIGH bulguları düzelt, sonra yeniden çağır.

- [ ] **Step 5: Commit**

```bash
git add docs/analysis/kolayik-video/faz1a-durum-takibi.md
git commit -m "docs: rapor ekrani durum takibi"
```

- [ ] **Step 6: Teslim — kullanıcıya sor**

Push ve `dev`'e PR açmak dış etkili adımlardır: kullanıcıdan onay al (branch `a/rapor-ekrani`, PR hedefi `dev`). Aynı oturumda: org kuralı gereği tasarım satırı (`design-log.md`: tarih, proje `senseik`, palet = mevcut Mantine/indigo, signature = "rapor tam sayfa grid") ve varsa genelleşen ders (`Excel'i tabloya geri okuyan port` deseni) için `ders-kaydet` skill'ini öner; kullanıcı onaylamadan framework repo'suna PR açma.
