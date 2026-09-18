using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Modules.OccupationalSafety.Domain.RiskAssessments;
using Sense.Modules.OccupationalSafety.Infrastructure.Persistence;

namespace Sense.Api.SeedData;

/// <summary>
/// senseik-015 (İSG modülü, Faz 0-1) demo seed: a hazard classification and a matching risk assessment for each of
/// the demo tenant's 3 locations - card senseik-015's merge_gate ("demo seed: tehlike sınıfı ve periyodlar yüklü").
/// Faz 2-6 (committee, accident/near-miss, PPE, document-engine integration) have no demo data yet - see the
/// module's handoff doc.
/// </summary>
public partial class DataSeeder
{
    private void SeedDemoOhs(Guid tenantId, Guid istanbulHqId, Guid ankaraOfficeId, Guid izmirOfficeId)
    {
        logger.LogInformation("Seeding OHS demo data (hazard classifications + risk assessments)...");

        // İstanbul HQ (office work): Az tehlikeli. Ankara Office: Tehlikeli (mixed use, e.g. an on-site workshop).
        // İzmir Office: Çok tehlikeli (e.g. a manufacturing/production site) - deliberately covers all 3 classes so
        // the settings pages show every renewal-period tier out of the box.
        var istanbulClassification = CreateHazardClassificationOrThrow(tenantId, istanbulHqId, HazardClass.Low, new DateOnly(2024, 1, 1), "Ofis çalışması");
        var ankaraClassification = CreateHazardClassificationOrThrow(tenantId, ankaraOfficeId, HazardClass.Medium, new DateOnly(2024, 1, 1), "Atölye içerir");
        var izmirClassification = CreateHazardClassificationOrThrow(tenantId, izmirOfficeId, HazardClass.High, new DateOnly(2024, 1, 1), "Üretim tesisi");
        ohsCtx.HazardClassifications.AddRange(istanbulClassification, ankaraClassification, izmirClassification);

        var istanbulAssessment = CreateRiskAssessmentOrThrow(
            tenantId, istanbulHqId, HazardClass.Low, new DateOnly(2024, 2, 1), "ABC OSGB", "Yıllık genel risk değerlendirmesi");
        var ankaraAssessment = CreateRiskAssessmentOrThrow(
            tenantId, ankaraOfficeId, HazardClass.Medium, new DateOnly(2024, 2, 1), "ABC OSGB", "Atölye ekipman riskleri dahil");
        var izmirAssessment = CreateRiskAssessmentOrThrow(
            tenantId, izmirOfficeId, HazardClass.High, new DateOnly(2024, 2, 1), "ABC OSGB", "Üretim hattı risk değerlendirmesi");
        ohsCtx.RiskAssessments.AddRange(istanbulAssessment, ankaraAssessment, izmirAssessment);
    }

    internal static WorkplaceHazardClassification CreateHazardClassificationOrThrow(
        Guid tenantId, Guid locationId, HazardClass hazardClass, DateOnly effectiveFrom, string? note)
    {
        var result = WorkplaceHazardClassification.Create(tenantId, locationId, hazardClass, effectiveFrom, note);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed hazard classification for location '{locationId}': {result.Error}");
        }

        return result.Value;
    }

    internal static RiskAssessment CreateRiskAssessmentOrThrow(
        Guid tenantId, Guid locationId, HazardClass hazardClassAtAssessment, DateOnly assessmentDate, string preparedBy, string? note)
    {
        var result = RiskAssessment.Create(tenantId, locationId, hazardClassAtAssessment, assessmentDate, preparedBy, note);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed risk assessment for location '{locationId}': {result.Error}");
        }

        return result.Value;
    }
}
