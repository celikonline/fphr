namespace Sense.Modules.Performance.Domain.ReviewCycles;

/// <summary>
/// "Yetkinlik" (competency questions only) or "Yetkinlik + Hedef" (adds the goal sub-flow). Only <see cref="Competency"/>
/// is implemented so far - the goal sub-flow (Goal entity, AddGoal/ApproveGoals/...) is a follow-up; see plan deviation #3.
/// </summary>
public enum ReviewKind
{
    Competency = 0,
    CompetencyAndGoal = 1,
}

/// <summary>Evaluator category (Kendisi/Üstü/Astı/Diğer). "External" from the design doc is folded into Peer for MVP.</summary>
public enum EvaluatorRelation
{
    Self = 0,
    Manager = 1,
    Subordinate = 2,
    Peer = 3,
}

public enum CommentMode
{
    Optional = 0,
    Required = 1,
    Disabled = 2,
}

public enum ReviewCycleStatus
{
    Draft = 0,
    Published = 1,
    Completed = 2,
}

/// <summary>How a completed cycle got there - natural (every assignment finished) or HR's ForceComplete.</summary>
public enum ReviewCompletionMode
{
    Natural = 0,
    Forced = 1,
}

public enum ReviewParticipantStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
}

public enum ReviewAssignmentStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Skipped = 3,
}

/// <summary>0-100 puan vs 5'li katılım ölçeği (raporda gösterilen ölçek etiketi, seed'siz sabit liste - bkz. plan sadeleştirmesi).</summary>
public enum RatingScale
{
    ZeroToHundred = 0,
    FivePointAgreement = 1,
}

/// <summary>Raporda değerlendirici adı mı yoksa yalnız kategori adı mı ("Üstü", "Diğer"...) görünsün.</summary>
public enum EvaluatorLabelMode
{
    Name = 0,
    CategoryOnly = 1,
}
