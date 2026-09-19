using Sense.Shared.Contracts.Security;

namespace Sense.Modules.OccupationalSafety.Contracts;

/// <summary>
/// Occupational Safety (İSG, senseik-015) module permission definitions. Keys are exactly
/// module.resource.action (3 segments, snake_case) per Sense.Tests.Architecture.OnionArchitectureTests.
/// PermissionKeys_FollowNamingConvention. Phase 0-1 scope only (hazard classification + risk assessments) -
/// committee/accident/PPE permissions are added in their own later phases (see the handoff doc).
/// </summary>
public static class OhsPermissions
{
    private const string Module = "ohs";
    private const string Group = "OccupationalSafety";

    public static readonly Permission HazardClassificationsView = new(
        "ohs.hazard_classifications.view", Module, "View Hazard Classifications", Group,
        "View the workplace hazard class assigned to each location");

    public static readonly Permission HazardClassificationsManage = new(
        "ohs.hazard_classifications.manage", Module, "Manage Hazard Classifications", Group,
        "Assign or update the workplace hazard class for a location");

    public static readonly Permission RiskAssessmentsView = new(
        "ohs.risk_assessments.view", Module, "View Risk Assessments", Group,
        "View recorded risk assessments and their renewal due dates");

    public static readonly Permission RiskAssessmentsManage = new(
        "ohs.risk_assessments.manage", Module, "Manage Risk Assessments", Group,
        "Record new risk assessments");

    public static IEnumerable<Permission> All =>
    [
        HazardClassificationsView, HazardClassificationsManage,
        RiskAssessmentsView, RiskAssessmentsManage,
    ];
}
