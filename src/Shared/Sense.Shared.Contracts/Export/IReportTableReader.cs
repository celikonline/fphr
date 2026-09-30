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
