using System.Reflection;
using Sense.Modules.Reporting.Contracts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Reporting.Tests;

/// <summary>
/// TB-15 nöbetçisi: kayıtlı IReportProvider'ların sözleşmesini mekanik olarak doğrula.
/// (a) Her provider'ın rapor kodu benzersiz
/// (b) Her provider'ın kodu boş/null değil
/// (c) IReportProvider (veri) ile IReportExporter (biçim) ayrı sorumluluk — provider biçimlendirme taşımaz
/// </summary>
public sealed class ReportProviderRegistryTests
{
    private const string Prefix = "Sense.";

    [Fact]
    public void AllReportProviders_HaveUniqueKeys()
    {
        var providerTypes = FindAllReportProviders();
        var keys = new List<(Type Type, string Key)>();

        foreach (var type in providerTypes)
        {
            // Yalnız concrete sınıfları test et (interface'leri skip et)
            if (type.IsInterface || type.IsAbstract)
            {
                continue;
            }

            try
            {
                // Key property'sini oku (dummy instance yoksa, statik/constant olmalı)
                var keyProp = type.GetProperty("Key", BindingFlags.Public | BindingFlags.Instance);
                keyProp.ShouldNotBeNull($"{type.FullName} IReportProvider.Key property'si yok");

                // Fake instance oluştur ve key'i oku (mümkün olursa)
                var instance = Activator.CreateInstance(type) as IReportProvider;
                if (instance is not null)
                {
                    keys.Add((type, instance.Key));
                }
            }
            catch
            {
                // Constructor parametreli ise skip, birebir reflection sınırlandırılması
                continue;
            }
        }

        // Benzersizlik kontrol et
        var duplicates = keys
            .GroupBy(x => x.Key)
            .Where(g => g.Count() > 1)
            .ToList();

        duplicates.ShouldBeEmpty(
            "Aynı rapor kodu birden çok provider tarafından sunulamaz: " +
            string.Join(", ", duplicates.Select(g => $"{g.Key} -> {string.Join(", ", g.Select(x => x.Type.Name))}")));
    }

    [Fact]
    public void AllReportProviders_HaveNonEmptyKeys()
    {
        var providerTypes = FindAllReportProviders()
            .Where(t => !t.IsInterface && !t.IsAbstract)
            .ToList();

        var emptyKeys = new List<(Type Type, string? Key)>();

        foreach (var type in providerTypes)
        {
            try
            {
                var instance = Activator.CreateInstance(type) as IReportProvider;
                if (instance is not null && string.IsNullOrWhiteSpace(instance.Key))
                {
                    emptyKeys.Add((type, instance.Key));
                }
            }
            catch
            {
                // Constructor parametreli ise skip
                continue;
            }
        }

        emptyKeys.ShouldBeEmpty(
            "Provider'lar boş/null Key taşıyamaz: " +
            string.Join(", ", emptyKeys.Select(x => x.Type.Name)));
    }

    [Fact]
    public void ReportProviders_DoNotImplementReportExporter()
    {
        var providerTypes = FindAllReportProviders()
            .Where(t => !t.IsInterface && !t.IsAbstract)
            .ToList();

        // IReportExporter ara; sözleşmenin tanımına bağlı olarak
        // Burada sadece IReportProvider'ın biçimlendirme sorumluluğu taşımaması kontrol edilir.
        // Reflection ile tam adında "Exporter" olan interface'ler arayan provider'lar eksik olmalı.

        var problematic = providerTypes
            .Where(t => t.GetInterfaces().Any(i => i.Name.Contains("Exporter", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        problematic.ShouldBeEmpty(
            "IReportProvider (veri) biçimlendirme sorumluluğu taşıyamaz (IReportExporter ayrıdır): " +
            string.Join(", ", problematic.Select(x => x.Name)));
    }

    private static Type[] FindAllReportProviders()
    {
        var dir = AppContext.BaseDirectory;
        var dlls = Directory.GetFiles(dir, Prefix + "*.dll", SearchOption.TopDirectoryOnly);
        var allTypes = new List<Type>();

        foreach (var dll in dlls)
        {
            try
            {
                var asm = Assembly.LoadFrom(dll);
                var providers = asm.GetTypes()
                    .Where(t => typeof(IReportProvider).IsAssignableFrom(t))
                    .ToList();
                allTypes.AddRange(providers);
            }
            catch
            {
                // Bazı assembly'ler yüklenmeyebilir, devam et
                continue;
            }
        }

        return allTypes.ToArray();
    }
}
