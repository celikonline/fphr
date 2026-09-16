using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Recruitment.Application;
using Sense.Modules.Recruitment.Application.Applications;
using Sense.Modules.Recruitment.Application.Candidates;
using Sense.Modules.Recruitment.Application.Public;
using Sense.Modules.Recruitment.Contracts;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Modules.Recruitment.Domain.FormResponses;
using Sense.Modules.Recruitment.Domain.FormTemplates;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Modules.Recruitment.Infrastructure;
using Sense.Modules.Recruitment.Infrastructure.Persistence;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

/// <summary>
/// Real PostgreSQL (Testcontainers): registers the module exactly like <c>RecruitmentModule.AddModule</c> so DI graph
/// mistakes (the R-1/TB-5 class of bug: a handler ctor asking for a repository nobody registered) show up here, not
/// only at Api startup. Postings are built directly via the domain factory (not <c>CreateJobPostingCommand</c>),
/// which needs Organization/Identity's IOrgUnitLookup/ILocationLookup/IUserAccountLookup - out of scope for a
/// Recruitment-only fixture; publishing/applying/moving-stage don't need those ports. Each test uses a fresh tenant
/// id - no ResetDatabaseAsync (the "recruitment" schema isn't in TestFixture's Respawn list yet).
/// </summary>
[Collection("PostgreSQL")]
public sealed class RecruitmentIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        RegisterRecruitmentModule(services, configuration);
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<RecruitmentDbContext>().Database.MigrateAsync();

    private static void RegisterRecruitmentModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<RecruitmentDbContext>(configuration, RecruitmentDbContext.SchemaName);
        services.AddModuleHandlers(
            RecruitmentDbContext.SchemaName,
            typeof(RecruitmentErrors).Assembly,
            typeof(RecruitmentDbContext).Assembly,
            typeof(IJobPostingRepository).Assembly,
            typeof(RecruitmentPermissions).Assembly);

        services.AddScoped<IJobPostingRepository, JobPostingRepository>();
        services.AddScoped<ICandidateRepository, CandidateRepository>();
        services.AddScoped<IJobApplicationRepository, JobApplicationRepository>();
        services.AddScoped<IJobRequisitionRepository, JobRequisitionRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IRecruitmentFormTemplateRepository, RecruitmentFormTemplateRepository>();
        services.AddScoped<IRecruitmentFormResponseRepository, RecruitmentFormResponseRepository>();
        services.AddScoped<IAutomationRuleRepository, AutomationRuleRepository>();
        services.AddRecruitmentCoreServices();
    }

    /// <summary>
    /// R-1/TB-5 regression guard: builds the exact same service graph RecruitmentModule.AddModule wires up and
    /// resolves MoveJobApplicationStageHandler's scope, proving IAutomationRuleRepository/IRecruitmentFormResponseRepository
    /// are really registered - not just present in a ctor signature that would otherwise only fail the first time
    /// someone calls the endpoint.
    /// </summary>
    [Fact]
    public void ModuleRegistration_ResolvesMoveJobApplicationStageHandler()
    {
        var configuration = ServiceProvider!.GetRequiredService<IConfiguration>();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddSenseCore(configuration);
        RegisterRecruitmentModule(services, configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        // Would throw InvalidOperationException here before R-1 was closed (no DbSet/repo for the two newly-added
        // ctor params) - Handle() itself needs a request/tenant scope, so resolution alone is the assertion.
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<MoveJobApplicationStageCommand>>();
        handler.ShouldNotBeNull();
        handler.ShouldBeOfType<MoveJobApplicationStageHandler>();
    }

    [Fact]
    public async Task MoveJobApplicationStage_GatedByActiveAutomationRule_BlocksUntilFormSubmitted()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "recruiter@example.com", new HashSet<string> { "HR" });

        var postingId = await CreatePublishedPostingAsync(tenantId);
        var candidateResult = await ExecuteCommandAsync(new CreateCandidateCommand("Ada", "Yılmaz", "ada.yilmaz@example.com", null));
        candidateResult.IsSuccess.ShouldBeTrue();

        var applicationResult = await ExecuteCommandAsync(new CreateJobApplicationCommand(postingId, candidateResult.Value));
        applicationResult.IsSuccess.ShouldBeTrue();
        var applicationId = applicationResult.Value;

        Guid templateId;
        FormFieldSnapshot field;
        await using (var scope = ServiceProvider!.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var template = RecruitmentFormTemplate.Create(
                tenantId, "Mülakat Formu", RecruitmentFormTemplateType.Feedback,
                [new FormFieldInput(null, "Notlar", FormFieldType.Paragraph, true, false, null, null)]).Value;
            db.FormTemplates.Add(template);
            await db.SaveChangesAsync();
            templateId = template.Id;
            field = new FormFieldSnapshot(template.Fields[0].Id);
        }

        var ruleResult = await ExecuteCommandAsync(
            new Sense.Modules.Recruitment.Application.AutomationRules.CreateAutomationRuleCommand("Screening", "Interview", templateId));
        ruleResult.IsSuccess.ShouldBeTrue();

        // Gate is on the Screening -> Interview transition; first move Applied -> Screening (ungated).
        (await ExecuteCommandAsync(new MoveJobApplicationStageCommand(applicationId, "Screening"))).IsSuccess.ShouldBeTrue();

        var blocked = await ExecuteCommandAsync(new MoveJobApplicationStageCommand(applicationId, "Interview"));
        blocked.IsFailure.ShouldBeTrue();
        blocked.Error.Code.ShouldBe(RecruitmentErrors.RequiredFormMissing);

        await using (var scope = ServiceProvider!.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var template = await db.FormTemplates.SingleAsync(t => t.Id == templateId);
            var response = RecruitmentFormResponse.Submit(
                tenantId, template, applicationId, Guid.NewGuid(), new Dictionary<Guid, string?> { [field.Id] = "Tamam" }).Value;
            db.FormResponses.Add(response);
            await db.SaveChangesAsync();
        }

        var allowed = await ExecuteCommandAsync(new MoveJobApplicationStageCommand(applicationId, "Interview"));
        allowed.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task PublicCareerFlow_SubmitApplication_ResolvesTenantFromSlug_AndRejectsDuplicateApply()
    {
        var tenantId = Guid.NewGuid();
        var postingId = await CreatePublishedPostingAsync(tenantId);
        var applySlug = await GetApplySlugAsync(postingId);

        // No tenant scope opened here on purpose - this is the anonymous career-site caller.
        var detail = await ExecuteQueryAsync(new GetPublicJobPostingQuery(applySlug));
        detail.IsSuccess.ShouldBeTrue();
        detail.Value.ApplySlug.ShouldBe(applySlug);

        var first = await ExecuteCommandAsync(
            new SubmitCareerApplicationCommand(applySlug, "Elif", "Kara", "elif.kara@example.com", "+905551112233", "2026-09", true));
        first.IsSuccess.ShouldBeTrue();

        var duplicate = await ExecuteCommandAsync(
            new SubmitCareerApplicationCommand(applySlug, "Elif", "Kara", "elif.kara@example.com", null, "2026-09", true));
        duplicate.IsFailure.ShouldBeTrue();
        duplicate.Error.Code.ShouldBe(RecruitmentDomainErrors.ApplicationAlreadyExists);

        await using var scope = ServiceProvider!.CreateAsyncScope();
        using var readTenantScope = TenantContextSetter!.BeginScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var candidate = await db.Candidates.SingleAsync(c => c.Email == "elif.kara@example.com");
        candidate.TenantId.ShouldBe(tenantId);
        candidate.Source.ShouldBe(CandidateSource.CareerSite);
    }

    [Fact]
    public async Task PublicCareerFlow_DraftPosting_IsNotPubliclyVisible()
    {
        var tenantId = Guid.NewGuid();
        Guid postingId;
        await using (var scope = ServiceProvider!.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var posting = JobPosting.Create(tenantId, "Gizli İlan", Guid.NewGuid(), null, RecruitmentEmploymentType.FullTime, null, Guid.NewGuid(), null).Value;
            db.JobPostings.Add(posting);
            await db.SaveChangesAsync();
            postingId = posting.Id;
        }

        var applySlug = await GetApplySlugAsync(postingId);

        var detail = await ExecuteQueryAsync(new GetPublicJobPostingQuery(applySlug));
        detail.IsFailure.ShouldBeTrue();
        detail.Error.Code.ShouldBe(RecruitmentDomainErrors.PostingNotFound);
    }

    [Fact]
    public async Task AnonymizeCandidate_ScrubsPersonalData_AndIsIdempotencyGuarded()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "hr@example.com", new HashSet<string> { "HR" });

        var candidateResult = await ExecuteCommandAsync(new CreateCandidateCommand("Ada", "Yılmaz", "ada.yilmaz@example.com", "5551234567"));
        candidateResult.IsSuccess.ShouldBeTrue();
        var candidateId = candidateResult.Value;

        var anonymized = await ExecuteCommandAsync(new AnonymizeCandidateCommand(candidateId));
        anonymized.IsSuccess.ShouldBeTrue();

        var dto = await ExecuteQueryAsync(new GetCandidateQuery(candidateId));
        dto.IsSuccess.ShouldBeTrue();
        dto.Value.IsAnonymized.ShouldBeTrue();
        dto.Value.Email.ShouldEndWith(Candidate.AnonymizedEmailDomain);

        // KVKK imha idempotent olmalı - ikinci çağrı sessizce yeniden silmez, açık bir hata döner.
        var second = await ExecuteCommandAsync(new AnonymizeCandidateCommand(candidateId));
        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe(RecruitmentDomainErrors.CandidateAlreadyAnonymized);
    }

    [Fact]
    public async Task PublicCareerFlow_RelatedPostings_OnlyReturnsSameTenantOpenPostings()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        var anchorId = await CreatePublishedPostingAsync(tenantId, "İlan A");
        var siblingId = await CreatePublishedPostingAsync(tenantId, "İlan B");
        await CreatePublishedPostingAsync(otherTenantId, "Başka Şirketin İlanı");

        var anchorSlug = await GetApplySlugAsync(anchorId);
        var siblingSlug = await GetApplySlugAsync(siblingId);

        var related = await ExecuteQueryAsync(new ListRelatedPublicJobPostingsQuery(anchorSlug));
        related.IsSuccess.ShouldBeTrue();
        related.Value.Count.ShouldBe(1);
        related.Value[0].ApplySlug.ShouldBe(siblingSlug);
    }

    /// <summary>
    /// Faz 9 tur 3 TALIMAT madde 1 (public uç tenant sızıntı testi): A kiracısının başvurusu B kiracısının aynı
    /// e-postalı adayıyla/başvurusuyla karışmaz - <see cref="ICandidateRepository.FindByEmailAsync"/> ambient
    /// tenant scope'a göre filtrelendiği için (bkz. <see cref="SubmitCareerApplicationHandler"/>), aynı e-posta iki
    /// farklı kiracıda iki ayrı Candidate satırı üretir ve "zaten başvurdunuz" hatası yalnızca kendi kiracısı
    /// içinde tetiklenir.
    /// </summary>
    [Fact]
    public async Task PublicCareerFlow_SameEmail_DifferentTenants_DoesNotLeakOrBlockAcrossTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var postingA = await CreatePublishedPostingAsync(tenantA, "Tenant A İlanı");
        var postingB = await CreatePublishedPostingAsync(tenantB, "Tenant B İlanı");
        var slugA = await GetApplySlugAsync(postingA);
        var slugB = await GetApplySlugAsync(postingB);

        const string sharedEmail = "paylasilan@example.com";

        var applyA = await ExecuteCommandAsync(
            new SubmitCareerApplicationCommand(slugA, "Ayşe", "Demir", sharedEmail, null, "2026-09", true));
        applyA.IsSuccess.ShouldBeTrue();

        // Same e-mail, other tenant's posting - must succeed independently, not "already applied".
        var applyB = await ExecuteCommandAsync(
            new SubmitCareerApplicationCommand(slugB, "Ayşe", "Demir", sharedEmail, null, "2026-09", true));
        applyB.IsSuccess.ShouldBeTrue();

        await using var scope = ServiceProvider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();

        using (TenantContextSetter!.BeginScope(tenantA))
        {
            var candidatesInA = await db.Candidates.Where(c => c.Email == sharedEmail).ToListAsync();
            candidatesInA.Count.ShouldBe(1);
            candidatesInA[0].TenantId.ShouldBe(tenantA);
        }

        using (TenantContextSetter!.BeginScope(tenantB))
        {
            var candidatesInB = await db.Candidates.Where(c => c.Email == sharedEmail).ToListAsync();
            candidatesInB.Count.ShouldBe(1);
            candidatesInB[0].TenantId.ShouldBe(tenantB);
        }
    }

    /// <summary>Yayında olmayan (Draft/Closed/Archived) bir ilanın slug'ı başka bir kiracının yayındaki ilanına asla eşleşmez - her slug tekil.</summary>
    [Fact]
    public async Task PublicCareerFlow_Apply_UnknownSlug_ReturnsNotFound_NoTenantHintLeaked()
    {
        var apply = await ExecuteCommandAsync(
            new SubmitCareerApplicationCommand("olmayan-slug-xyz", "Test", "Kullanıcı", "test@example.com", null, "2026-09", true));
        apply.IsFailure.ShouldBeTrue();
        apply.Error.Code.ShouldBe(RecruitmentDomainErrors.PostingNotFound);
    }

    private async Task<Guid> CreatePublishedPostingAsync(Guid tenantId, string title = "Backend Geliştirici")
    {
        Guid postingId;
        await using (var scope = ServiceProvider!.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var posting = JobPosting.Create(tenantId, title, Guid.NewGuid(), null, RecruitmentEmploymentType.FullTime, "Açıklama", Guid.NewGuid(), null).Value;
            posting.Publish();
            db.JobPostings.Add(posting);
            await db.SaveChangesAsync();
            postingId = posting.Id;
        }

        return postingId;
    }

    private async Task<string> GetApplySlugAsync(Guid postingId)
    {
        await using var scope = ServiceProvider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return (await db.JobPostings.IgnoreQueryFilters().SingleAsync(p => p.Id == postingId)).ApplySlug;
    }

    private sealed record FormFieldSnapshot(Guid Id);
}
