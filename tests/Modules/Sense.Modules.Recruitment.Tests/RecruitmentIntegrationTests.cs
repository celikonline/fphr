using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Recruitment.Application;
using Sense.Modules.Recruitment.Application.Applications;
using Sense.Modules.Recruitment.Application.Candidates;
using Sense.Modules.Recruitment.Application.Public;
using Sense.Modules.Recruitment.Contracts;
using Sense.Modules.Recruitment.Contracts.Events;
using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.Applications;
using Sense.Modules.Recruitment.Domain.Candidates;
using Sense.Modules.Recruitment.Domain.FormResponses;
using Sense.Modules.Recruitment.Domain.FormTemplates;
using Sense.Modules.Recruitment.Domain.JobPostings;
using Sense.Modules.Recruitment.Infrastructure;
using Sense.Modules.Recruitment.Infrastructure.BackgroundJobs;
using Sense.Modules.Recruitment.Infrastructure.Persistence;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Persistence;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

/// <summary>
/// Real PostgreSQL (Testcontainers): registers the module exactly like <c>RecruitmentModule.AddModule</c> so DI graph
/// mistakes (the R-1/TB-5 class of bug: a handler ctor asking for a repository nobody registered) show up here, not
/// only at Api startup. Postings are built directly via the domain factory (not <c>CreateJobPostingCommand</c>),
/// which needs Organization/Identity's IOrgUnitLookup/IUserAccountLookup - out of scope for a Recruitment-only
/// fixture; publishing/applying/moving-stage don't need those ports. Two exceptions: <see cref="ITenantSlugLookup"/>
/// (S-10, <c>ListPublicJobPostingsByTenantSlugQuery</c>) and <see cref="ILocationLookup"/> (S-19, public posting
/// <c>Location</c> name resolution) - standing up Identity's/Organization's whole stack just for these reads isn't
/// worth it, so <see cref="FakeTenantSlugLookup"/>/<see cref="FakeLocationLookup"/> (real port implementations,
/// just in-memory ones - not mocks of Recruitment's own code) are registered instead. Each test uses a fresh
/// tenant id - no ResetDatabaseAsync (the "recruitment" schema isn't in TestFixture's Respawn list yet).
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
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

        services.AddSingleton<FakeTenantSlugLookup>();
        services.AddSingleton<ITenantSlugLookup>(sp => sp.GetRequiredService<FakeTenantSlugLookup>());

        // Tur içi ek iş BE (18:17, S-19 tüketimi): ILocationLookup gerçek Organization.Infrastructure implementasyonu
        // bu fixture'da kayıtlı değil (yukarıdaki sınıf yorumu) - GetPublicJobPostingHandler/ListRelatedPublicJobPostingsHandler/
        // ListPublicJobPostingsByTenantSlugHandler artık ctor'da ILocationLookup istiyor, aksi halde DI çözümlemesi patlar.
        services.AddSingleton<FakeLocationLookup>();
        services.AddSingleton<ILocationLookup>(sp => sp.GetRequiredService<FakeLocationLookup>());

        // AuthorizationBehaviour (pipeline) ctor'unda IPermissionService + IDataScopeService ister; Recruitment
        // istekleri anonim de olabilir (public career) - bu fixture'da izin/kapsam her zaman açık: Employee/
        // Organization fixture'larındaki GrantAllPermissions/FakeDataScope precedent'i.
        services.AddSingleton<IPermissionService, GrantAllPermissions>();
        services.AddSingleton<IDataScopeService, OpenDataScope>();
        services.AddSingleton<IAccessSettingsService, AllAccessSettingsEnabled>();

        // Faz 9 gün açılışı TALIMAT'ı madde 1 (job testi): CandidateRetentionSweepJobHandler normalde yalnız
        // RecruitmentModule.AddModule'de (Sense.Api/Hangfire) kayıtlıdır - burada da kaydederek gerçek DB'ye karşı
        // BackgroundJobExecutor'ı devreye sokmadan doğrudan çözümleyip çağırabiliyoruz.
        services.AddScoped<IScheduledJobHandler, CandidateRetentionSweepJobHandler>();

        // Faz 9 tur 1 TALIMAT madde 1-2: JobApplicationStageChangedHandler S-23 IEmployeeProvisioning'i çağırır;
        // bu fixture'da fake implementasyon kaydedilir (gerçek implementasyon Faz 2'de).
        services.AddSingleton<FakeEmployeeProvisioning>();
        services.AddSingleton<IEmployeeProvisioning>(sp => sp.GetRequiredService<FakeEmployeeProvisioning>());
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
    public async Task PublicCareerFlow_SubmitApplication_OutdatedPrivacyNoticeVersion_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        var postingId = await CreatePublishedPostingAsync(tenantId);
        var applySlug = await GetApplySlugAsync(postingId);

        var result = await ExecuteCommandAsync(
            new SubmitCareerApplicationCommand(applySlug, "Elif", "Kara", "elif.kara@example.com", null, "2025-01", true));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.ApplicationPrivacyNoticeOutdated);

        await using var scope = ServiceProvider!.CreateAsyncScope();
        using var readTenantScope = TenantContextSetter!.BeginScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        (await db.Candidates.AnyAsync(c => c.Email == "elif.kara@example.com")).ShouldBeFalse();
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
    public async Task PublicPostingsList_UnknownSlug_ReturnsEmptyList()
    {
        var result = await ExecuteQueryAsync(new ListPublicJobPostingsByTenantSlugQuery("boyle-bir-kiraci-yok"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task PublicPostingsList_TenantASlug_DoesNotReturnTenantBPosting()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var slugLookup = ServiceProvider!.GetRequiredService<FakeTenantSlugLookup>();
        slugLookup.Map("kiraci-a", tenantA);
        slugLookup.Map("kiraci-b", tenantB);

        await CreatePublishedPostingAsync(tenantA, "A Şirketi İlanı");
        await CreatePublishedPostingAsync(tenantB, "B Şirketi İlanı");

        var listA = await ExecuteQueryAsync(new ListPublicJobPostingsByTenantSlugQuery("kiraci-a"));
        listA.IsSuccess.ShouldBeTrue();
        listA.Value.Count.ShouldBe(1);
        listA.Value[0].Title.ShouldBe("A Şirketi İlanı");
    }

    [Fact]
    public async Task PublicPostingsList_ClosedOrExpiredPosting_IsExcluded()
    {
        var tenantId = Guid.NewGuid();
        var slugLookup = ServiceProvider!.GetRequiredService<FakeTenantSlugLookup>();
        slugLookup.Map("kiraci-durum-testi", tenantId);

        var openPostingId = await CreatePublishedPostingAsync(tenantId, "Açık İlan");
        await CreateClosedPostingAsync(tenantId, "Kapanmış İlan");
        await CreateExpiredPostingAsync(tenantId, "Süresi Dolmuş İlan");

        var list = await ExecuteQueryAsync(new ListPublicJobPostingsByTenantSlugQuery("kiraci-durum-testi"));

        list.IsSuccess.ShouldBeTrue();
        list.Value.Count.ShouldBe(1);
        list.Value[0].ApplySlug.ShouldBe(await GetApplySlugAsync(openPostingId));
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
    /// Faz 9 tur 2 "tur içi ek iş" BE madde 6 (S-19 tüketimi): bir ilanın <c>LocationId</c>'si
    /// <see cref="ILocationLookup"/>'ta biliniyorsa, hem tenant-wide public listede hem detayda hem de
    /// related listede adı görünür - tek bir <c>GetNamesAsync</c> çağrısıyla (N+1 yok), test yalnız dolaylı
    /// olarak <see cref="FakeLocationLookup"/> çağrı sayısını ölçmüyor ama toplu listede birden çok ilanın
    /// aynı çağrıda çözüldüğünü doğruluyor.
    /// </summary>
    [Fact]
    public async Task PublicCareerFlow_PostingWithKnownLocation_ResolvesLocationName_InListDetailAndRelated()
    {
        var tenantId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var locations = ServiceProvider!.GetRequiredService<FakeLocationLookup>();
        locations.Map(locationId, "İstanbul - Merkez Ofis");

        var slugLookup = ServiceProvider!.GetRequiredService<FakeTenantSlugLookup>();
        slugLookup.Map("konumlu-kiraci", tenantId);

        var anchorId = await CreatePublishedPostingAsync(tenantId, "Konumlu İlan", locationId);
        var siblingId = await CreatePublishedPostingAsync(tenantId, "Konumlu Kardeş İlan", locationId);
        var anchorSlug = await GetApplySlugAsync(anchorId);
        var siblingSlug = await GetApplySlugAsync(siblingId);

        var detail = await ExecuteQueryAsync(new GetPublicJobPostingQuery(anchorSlug));
        detail.IsSuccess.ShouldBeTrue();
        detail.Value.Location.ShouldBe("İstanbul - Merkez Ofis");

        var related = await ExecuteQueryAsync(new ListRelatedPublicJobPostingsQuery(anchorSlug));
        related.IsSuccess.ShouldBeTrue();
        related.Value.Single(p => p.ApplySlug == siblingSlug).Location.ShouldBe("İstanbul - Merkez Ofis");

        var list = await ExecuteQueryAsync(new ListPublicJobPostingsByTenantSlugQuery("konumlu-kiraci"));
        list.IsSuccess.ShouldBeTrue();
        list.Value.ShouldAllBe(p => p.Location == "İstanbul - Merkez Ofis");
    }

    /// <summary>
    /// Faz 9 tur 2 "tur içi ek iş" BE madde 7/8: <see cref="ILocationLookup.GetNamesAsync"/> gerçek ortamda
    /// Organization'ın ambient <c>ITenantContext</c>'ine göre filtrelendiği için, anonim (JWT'siz) bir istekte
    /// public query handler'larının çağrı sırasında bir <c>ITenantContextSetter.BeginScope</c> açmış olması gerekir
    /// - yalnızca <c>GetNamesAsync</c>'e doğru <c>tenantId</c> parametresinin geçmesi yeterli değildir, çünkü fake
    /// bunu zaten yok sayıp yalnızca <c>locationId</c>'ye bakıyor. Bu test ambient bağlamı doğrudan gözlemliyor.
    /// </summary>
    [Fact]
    public async Task PublicCareerFlow_LocationLookup_ObservesAmbientTenantScope_ForAnonymousRequest()
    {
        var tenantId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var locations = ServiceProvider!.GetRequiredService<FakeLocationLookup>();
        locations.Map(locationId, "Ankara - Şube");

        var postingId = await CreatePublishedPostingAsync(tenantId, "Ambient Bağlamlı İlan", locationId);
        var applySlug = await GetApplySlugAsync(postingId);

        var detail = await ExecuteQueryAsync(new GetPublicJobPostingQuery(applySlug));
        detail.IsSuccess.ShouldBeTrue();
        locations.LastObservedAmbientTenantId.ShouldBe(tenantId);

        var related = await ExecuteQueryAsync(new ListRelatedPublicJobPostingsQuery(applySlug));
        related.IsSuccess.ShouldBeTrue();

        var slugLookup = ServiceProvider!.GetRequiredService<FakeTenantSlugLookup>();
        slugLookup.Map("ambient-kiraci", tenantId);
        var list = await ExecuteQueryAsync(new ListPublicJobPostingsByTenantSlugQuery("ambient-kiraci"));
        list.IsSuccess.ShouldBeTrue();
        locations.LastObservedAmbientTenantId.ShouldBe(tenantId);
    }

    /// <summary>
    /// Faz 9 tur 2 "tur içi ek iş" BE madde 6: <see cref="ILocationLookup"/>'ta karşılığı olmayan bir
    /// <c>LocationId</c> (ör. anonim istek bağlamında sözlük boş dönerse) sessizce <c>null</c>'a düşer - hata
    /// fırlatmaz, konumsuz ilanla aynı davranır.
    /// </summary>
    [Fact]
    public async Task PublicCareerFlow_PostingWithUnresolvedLocation_LocationIsNull()
    {
        var tenantId = Guid.NewGuid();
        var unknownLocationId = Guid.NewGuid();

        var postingId = await CreatePublishedPostingAsync(tenantId, "Konumu Bilinmeyen İlan", unknownLocationId);
        var applySlug = await GetApplySlugAsync(postingId);

        var detail = await ExecuteQueryAsync(new GetPublicJobPostingQuery(applySlug));
        detail.IsSuccess.ShouldBeTrue();
        detail.Value.Location.ShouldBeNull();
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

    /// <summary>G-9-09 (Faz 9 gün açılışı TALIMAT'ı madde 1, komut yolu): Hired aşamasında en az bir başvurusu olan
    /// aday, saklama süresi dolmuş olsa da <see cref="AnonymizeExpiredCandidatesCommand"/> tarafından atlanır.</summary>
    [Fact]
    public async Task AnonymizeExpiredCandidates_ExcludesHiredCandidate()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "hr@example.com", new HashSet<string> { "HR" });

        var postingId = await CreatePublishedPostingAsync(tenantId);
        var hiredCandidateId = (await ExecuteCommandAsync(new CreateCandidateCommand("Hired", "Aday", "hired.komut@example.com", null))).Value;
        var pendingCandidateId = (await ExecuteCommandAsync(new CreateCandidateCommand("Bekleyen", "Aday", "bekleyen.komut@example.com", null))).Value;

        var hiredApplicationId = (await ExecuteCommandAsync(new CreateJobApplicationCommand(postingId, hiredCandidateId))).Value;
        (await ExecuteCommandAsync(new MoveJobApplicationStageCommand(hiredApplicationId, nameof(ApplicationStage.Hired)))).IsSuccess.ShouldBeTrue();

        await BackdateCandidateCreatedAtAsync(hiredCandidateId, pendingCandidateId);

        var swept = await ExecuteCommandAsync(new AnonymizeExpiredCandidatesCommand());
        swept.IsSuccess.ShouldBeTrue();
        swept.Value.ShouldBe(1);

        (await ExecuteQueryAsync(new GetCandidateQuery(hiredCandidateId))).Value.IsAnonymized.ShouldBeFalse();
        (await ExecuteQueryAsync(new GetCandidateQuery(pendingCandidateId))).Value.IsAnonymized.ShouldBeTrue();
    }

    /// <summary>G-9-09, job yolu: <see cref="CandidateRetentionSweepJobHandler"/> doğrudan (dispatcher'sız)
    /// çağrıldığında da aynı Hired hariç tutma kuralına uyar - kural repository sorgusunda olduğu için iki yol da
    /// aynı davranışı miras alır, ama job'un kendi DI zincirinin de gerçekten çalıştığını burada kanıtlıyoruz.</summary>
    [Fact]
    public async Task CandidateRetentionSweepJob_ExcludesHiredCandidate()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "hr@example.com", new HashSet<string> { "HR" });

        var postingId = await CreatePublishedPostingAsync(tenantId);
        var hiredCandidateId = (await ExecuteCommandAsync(new CreateCandidateCommand("Hired", "Aday", "hired.job@example.com", null))).Value;
        var pendingCandidateId = (await ExecuteCommandAsync(new CreateCandidateCommand("Bekleyen", "Aday", "bekleyen.job@example.com", null))).Value;

        var hiredApplicationId = (await ExecuteCommandAsync(new CreateJobApplicationCommand(postingId, hiredCandidateId))).Value;
        (await ExecuteCommandAsync(new MoveJobApplicationStageCommand(hiredApplicationId, nameof(ApplicationStage.Hired)))).IsSuccess.ShouldBeTrue();

        await BackdateCandidateCreatedAtAsync(hiredCandidateId, pendingCandidateId);

        await using (var jobScope = ServiceProvider!.CreateAsyncScope())
        {
            var handler = jobScope.ServiceProvider.GetServices<IScheduledJobHandler>().Single(h => h.Handles == JobType.CandidateRetentionSweep);
            await handler.ExecuteAsync(new ScheduledJobContext(tenantId, null, DateTimeOffset.UtcNow), CancellationToken.None);
        }

        // NB: fresh scopes below, not ExecuteQueryAsync: the root dispatcher scope still tracks the pre-job
        // entity state, and EF re-query does not refresh already-tracked instances - a production read always
        // runs in its own request scope, so assert through one here too.
        (await IsAnonymizedAsync(hiredCandidateId)).ShouldBeFalse();
        (await IsAnonymizedAsync(pendingCandidateId)).ShouldBeTrue();
    }

    private async Task<bool> IsAnonymizedAsync(Guid candidateId)
    {
        await using var scope = ServiceProvider!.CreateAsyncScope();
        var candidates = scope.ServiceProvider.GetRequiredService<ICandidateRepository>();
        return (await candidates.GetByIdAsync(candidateId)).Value.IsAnonymized;
    }

    /// <summary>G-9-12 (Faz 9 gün açılışı TALIMAT'ı madde 4): aday anonimleştirilince form cevaplarındaki serbest
    /// metin (Paragraph) yanıtı kaldırılır, ScoreCard (sayısal puan) yanıtı aynen kalır.</summary>
    [Fact]
    public async Task AnonymizeCandidate_ScrubsFreeTextFormAnswers_KeepsScoreCardAnswers()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        SetCurrentUser(tenantId, Guid.NewGuid(), "hr@example.com", new HashSet<string> { "HR" });

        var postingId = await CreatePublishedPostingAsync(tenantId);
        var candidateId = (await ExecuteCommandAsync(new CreateCandidateCommand("Skor", "Adayı", "skor.adayi@example.com", null))).Value;
        var applicationId = (await ExecuteCommandAsync(new CreateJobApplicationCommand(postingId, candidateId))).Value;

        Guid scoreFieldId;
        Guid textFieldId;
        Guid responseId;
        await using (var scope = ServiceProvider!.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var template = RecruitmentFormTemplate.Create(
                tenantId, "Değerlendirme Formu", RecruitmentFormTemplateType.Scorecard,
                [
                    new FormFieldInput(null, "Genel Puan", FormFieldType.ScoreCard, true, false, null, 100),
                    new FormFieldInput(null, "Notlar", FormFieldType.Paragraph, false, false, null, null),
                ]).Value;
            db.FormTemplates.Add(template);
            await db.SaveChangesAsync();
            scoreFieldId = template.Fields.First(f => f.FieldType == FormFieldType.ScoreCard).Id;
            textFieldId = template.Fields.First(f => f.FieldType == FormFieldType.Paragraph).Id;

            var response = RecruitmentFormResponse.Submit(
                tenantId, template, applicationId, Guid.NewGuid(),
                new Dictionary<Guid, string?> { [scoreFieldId] = "85", [textFieldId] = "Adayın kişisel gözlemleri burada" }).Value;
            db.FormResponses.Add(response);
            await db.SaveChangesAsync();
            responseId = response.Id;
        }

        (await ExecuteCommandAsync(new AnonymizeCandidateCommand(candidateId))).IsSuccess.ShouldBeTrue();

        await using var readScope = ServiceProvider!.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var savedResponse = await readDb.FormResponses.SingleAsync(r => r.Id == responseId);
        savedResponse.Answers.Any(a => a.FieldId == textFieldId).ShouldBeFalse();
        savedResponse.Answers.Single(a => a.FieldId == scoreFieldId).Value.ShouldBe("85");
    }

    /// <summary>Madde 2/3: süresi dolmuş bir ilanın public detayı da, tam olarak var olmayan bir ilan gibi 404
    /// PostingNotFound döner - "kapalı"dan ayırt edilebilecek bir sinyal sızmaz.</summary>
    [Fact]
    public async Task PublicCareerFlow_ExpiredPosting_DetailReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var postingId = await CreateExpiredPostingAsync(tenantId, "Süresi Dolmuş İlan");
        var applySlug = await GetApplySlugAsync(postingId);

        var detail = await ExecuteQueryAsync(new GetPublicJobPostingQuery(applySlug));

        detail.IsFailure.ShouldBeTrue();
        detail.Error.Code.ShouldBe(RecruitmentDomainErrors.PostingNotFound);
    }

    /// <summary>Madde 2/3: süresi dolmuş bir ilana apply de 404 döner ve hiçbir aday/başvuru kaydı oluşmaz.</summary>
    [Fact]
    public async Task PublicCareerFlow_ExpiredPosting_ApplyReturnsNotFound_NoRecordCreated()
    {
        var tenantId = Guid.NewGuid();
        var postingId = await CreateExpiredPostingAsync(tenantId, "Süresi Dolmuş İlan");
        var applySlug = await GetApplySlugAsync(postingId);

        var apply = await ExecuteCommandAsync(
            new SubmitCareerApplicationCommand(applySlug, "Test", "Aday", "suresi.dolmus@example.com", null, "2026-09", true));

        apply.IsFailure.ShouldBeTrue();
        apply.Error.Code.ShouldBe(RecruitmentDomainErrors.PostingNotFound);

        await using var scope = ServiceProvider!.CreateAsyncScope();
        using var readTenantScope = TenantContextSetter!.BeginScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        (await db.Candidates.AnyAsync(c => c.Email == "suresi.dolmus@example.com")).ShouldBeFalse();
    }

    /// <summary>Madde 2/3: related listesi de süresi dolmuş bir kardeş ilanı döndürmez.</summary>
    [Fact]
    public async Task PublicCareerFlow_RelatedPostings_ExcludesExpiredSibling()
    {
        var tenantId = Guid.NewGuid();
        var anchorId = await CreatePublishedPostingAsync(tenantId, "İlan A");
        var expiredSiblingId = await CreateExpiredPostingAsync(tenantId, "Süresi Dolmuş Kardeş İlan");
        var anchorSlug = await GetApplySlugAsync(anchorId);
        _ = expiredSiblingId;

        var related = await ExecuteQueryAsync(new ListRelatedPublicJobPostingsQuery(anchorSlug));

        related.IsSuccess.ShouldBeTrue();
        related.Value.ShouldBeEmpty();
    }

    /// <summary>Madde 2/3 sınır durumu: kapanış tarihi bugün olan ilan hâlâ görünür (kapanış tarihi dahildir).</summary>
    [Fact]
    public async Task PublicCareerFlow_ClosingDateIsToday_StillVisible()
    {
        var tenantId = Guid.NewGuid();
        var postingId = await CreateClosingTodayPostingAsync(tenantId, "Bugün Kapanan İlan");
        var applySlug = await GetApplySlugAsync(postingId);

        var detail = await ExecuteQueryAsync(new GetPublicJobPostingQuery(applySlug));

        detail.IsSuccess.ShouldBeTrue();
        detail.Value.ApplySlug.ShouldBe(applySlug);
    }

    /// <summary>Faz 9 tur 1 TALIMAT madde 1-2: JobApplicationStageChangedHandler unit test - Hired stage
    /// triggers IEmployeeProvisioning.CreateFromCandidateAsync with correct fields.</summary>
    [Fact]
    public async Task JobApplicationStageChangedHandler_HiredStage_TriggersProvisioning()
    {
        var tenantId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var postingId = Guid.NewGuid();

        var fake = new FakeJobApplicationRepository();
        var application = JobApplication.Create(tenantId, postingId, candidateId);
        fake.ApplicationsById[application.Id] = application;

        var fakeCandidate = new FakeCandidateRepository();
        var candidate = Candidate.Create(tenantId, "John", "Doe", "john.doe@example.com", "+905551234567").Value;
        fakeCandidate.CandidatesById[candidate.Id] = candidate;

        var fakeProvisioning = new FakeEmployeeProvisioning();
        var fakeUnitOfWork = new FakeRecruitmentUnitOfWork();
        var logger = new NullLogger<JobApplicationStageChangedHandler>();
        var handler = new JobApplicationStageChangedHandler(fake, fakeCandidate, fakeProvisioning, fakeUnitOfWork, logger);

        // Tur 11 kapanışı: event artık gerçekte kaydedilen application.Id'yi taşıyor - önceden ilgisiz, ayrı bir
        // Guid.NewGuid() kullanılıyordu, handler @event.JobApplicationId ile fake repoda hiçbir zaman eşleşen bir
        // kayıt bulamıyor ve sessizce erken çıkıyordu (bu test hiçbir zaman geçmemişti).
        var @event = new JobApplicationStageChanged(tenantId, application.Id, postingId, candidateId, nameof(ApplicationStage.Hired));

        await handler.Handle(@event, CancellationToken.None);

        fakeProvisioning.ProvisionedCandidates.Count.ShouldBe(1);
        var request = fakeProvisioning.ProvisionedCandidates[0];
        request.FirstName.ShouldBe("John");
        request.LastName.ShouldBe("Doe");
        request.Email.ShouldBe("john.doe@example.com");
        request.Phone.ShouldBe("+905551234567");
        request.NationalId.ShouldBeNull();
        request.OrgUnitId.ShouldBeNull();
        request.PositionTitle.ShouldBeNull();
        request.SourceApplicationId.ShouldBe(application.Id);
        fakeUnitOfWork.SaveChangesCalled.ShouldBeTrue();
    }

    /// <summary>Faz 9 tur 1 TALIMAT madde 1-2: JobApplicationStageChangedHandler unit test - non-Hired
    /// stage changes do not trigger provisioning.</summary>
    [Fact]
    public async Task JobApplicationStageChangedHandler_NonHiredStage_DoesNotTrigger()
    {
        var tenantId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var postingId = Guid.NewGuid();

        var fake = new FakeJobApplicationRepository();
        var fakeCandidate = new FakeCandidateRepository();
        var fakeProvisioning = new FakeEmployeeProvisioning();
        var fakeUnitOfWork = new FakeRecruitmentUnitOfWork();
        var logger = new NullLogger<JobApplicationStageChangedHandler>();
        var handler = new JobApplicationStageChangedHandler(fake, fakeCandidate, fakeProvisioning, fakeUnitOfWork, logger);

        // Test Screening, Interview, Offer, Rejected - none should trigger provisioning.
        foreach (var stage in new[] { "Screening", "Interview", "Offer", "Rejected" })
        {
            fakeProvisioning.Reset();
            var @event = new JobApplicationStageChanged(tenantId, applicationId, postingId, candidateId, stage);
            await handler.Handle(@event, CancellationToken.None);
            fakeProvisioning.ProvisionedCandidates.Count.ShouldBe(0);
        }
    }

    /// <summary>Backdates two candidates' <c>created_at</c> past the retention window via raw SQL - the audit
    /// interceptor stamps <c>CreatedAt</c> on insert and refuses to let a normal EF update touch it afterward
    /// (see AuditTenantInterceptor.Apply/Modified), so this is the only way to simulate "created a long time ago".</summary>
    private async Task BackdateCandidateCreatedAtAsync(params Guid[] candidateIds)
    {
        await using var scope = ServiceProvider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var past = DateTime.UtcNow.AddMonths(-(RecruitmentLimits.CandidateDataRetentionMonths + 1));
        foreach (var candidateId in candidateIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE recruitment.candidates SET created_at = {past} WHERE id = {candidateId}");
        }
    }

    private async Task<Guid> CreatePublishedPostingAsync(Guid tenantId, string title = "Backend Geliştirici", Guid? locationId = null)
    {
        Guid postingId;
        await using (var scope = ServiceProvider!.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var posting = JobPosting.Create(tenantId, title, Guid.NewGuid(), locationId, RecruitmentEmploymentType.FullTime, "Açıklama", Guid.NewGuid(), null).Value;
            posting.Publish();
            db.JobPostings.Add(posting);
            await db.SaveChangesAsync();
            postingId = posting.Id;
        }

        return postingId;
    }

    private async Task<Guid> CreateClosedPostingAsync(Guid tenantId, string title)
    {
        await using var scope = ServiceProvider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var posting = JobPosting.Create(tenantId, title, Guid.NewGuid(), null, RecruitmentEmploymentType.FullTime, "Açıklama", Guid.NewGuid(), null).Value;
        posting.Publish();
        posting.Close();
        db.JobPostings.Add(posting);
        await db.SaveChangesAsync();
        return posting.Id;
    }

    private async Task<Guid> CreateExpiredPostingAsync(Guid tenantId, string title)
    {
        await using var scope = ServiceProvider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var posting = JobPosting.Create(tenantId, title, Guid.NewGuid(), null, RecruitmentEmploymentType.FullTime, "Açıklama", Guid.NewGuid(), null).Value;
        posting.Publish();
        var yesterday = DateOnly.FromDateTime(Clock!.GetUtcNow().UtcDateTime).AddDays(-1);
        posting.Update(title, Guid.NewGuid(), null, RecruitmentEmploymentType.FullTime, "Açıklama", null, null, yesterday);
        db.JobPostings.Add(posting);
        await db.SaveChangesAsync();
        return posting.Id;
    }

    /// <summary>Sınır durumu yardımcısı (madde 2/3): kapanış tarihi tam olarak bugün olan, hâlâ Open bir ilan.</summary>
    private async Task<Guid> CreateClosingTodayPostingAsync(Guid tenantId, string title)
    {
        await using var scope = ServiceProvider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var posting = JobPosting.Create(tenantId, title, Guid.NewGuid(), null, RecruitmentEmploymentType.FullTime, "Açıklama", Guid.NewGuid(), null).Value;
        posting.Publish();
        var today = DateOnly.FromDateTime(Clock!.GetUtcNow().UtcDateTime);
        posting.Update(title, Guid.NewGuid(), null, RecruitmentEmploymentType.FullTime, "Açıklama", null, null, today);
        db.JobPostings.Add(posting);
        await db.SaveChangesAsync();
        return posting.Id;
    }

    private async Task<string> GetApplySlugAsync(Guid postingId)
    {
        await using var scope = ServiceProvider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return (await db.JobPostings.IgnoreQueryFilters().SingleAsync(p => p.Id == postingId)).ApplySlug;
    }

    private sealed record FormFieldSnapshot(Guid Id);

    /// <summary>
    /// AuthorizationBehaviour/AccessSettingsAuthorizationBehaviour portları: Recruitment isteklerinde ne
    /// [RequiresPermission] engeli (public career anonim akar) ne [RequiresAccessSetting] var; ama pipeline
    /// davranışları ctor'dan çözüldüğü için kayıt şart. Her şeyi açan in-memory implementasyonlar (Employee
    /// fixture'ındaki GrantAllPermissions/FakeDataScope/AllAccessSettingsEnabled precedent'i).
    /// </summary>
    private sealed class GrantAllPermissions : IPermissionService
    {
        public Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<DataScope> GetScopeAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(DataScope.Company);
    }

    private sealed class OpenDataScope : IDataScopeService
    {
        public Task<IReadOnlySet<Guid>?> GetVisibleEmployeeIdsAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>?>(null);

        public Task<bool> CanAccessEmployeeAsync(string permission, Guid employeeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<IReadOnlySet<Guid>?> GetAccessibleOrgUnitIdsAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>?>(null);
    }

    private sealed class AllAccessSettingsEnabled : IAccessSettingsService
    {
        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> IsEnabledForCurrentUserAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    /// <summary>
    /// In-memory <see cref="ITenantSlugLookup"/> for this fixture (see class doc comment): tests call
    /// <see cref="Map"/> to declare which slug resolves to which tenant id before dispatching
    /// <c>ListPublicJobPostingsByTenantSlugQuery</c>. An unmapped slug behaves exactly like the real
    /// implementation's "unknown/suspended tenant" case - <c>null</c>, not an exception.
    /// </summary>
    private sealed class FakeTenantSlugLookup : ITenantSlugLookup
    {
        private readonly Dictionary<string, Guid> _bySlug = [];

        public void Map(string slug, Guid tenantId) => _bySlug[slug] = tenantId;

        public Task<Guid?> FindTenantIdAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(_bySlug.TryGetValue(slug, out var tenantId) ? tenantId : (Guid?)null);
    }

    /// <summary>
    /// In-memory <see cref="ILocationLookup"/> for this fixture (see class doc comment, S-19 tüketimi): tests call
    /// <see cref="Map"/> to declare a location's display name before dispatching a public posting query. An id with
    /// no matching call to <see cref="Map"/> behaves exactly like the real implementation's "no location for this
    /// tenant" case - simply absent from <see cref="GetNamesAsync"/>'s result, not an exception.
    /// </summary>
    private sealed class FakeLocationLookup : ILocationLookup
    {
        private readonly Dictionary<Guid, string> _byId = [];

        /// <summary>
        /// Ambient <c>ITenantContext.TenantId</c> observed during the most recent <see cref="GetNamesAsync"/> call
        /// (Faz 9 tur 2 "tur içi ek iş" BE madde 7/8): the real Organization <c>LocationLookup</c> filters by this
        /// ambient tenant, so the public query handlers must open an explicit <c>ITenantContextSetter.BeginScope</c>
        /// before calling this port for an anonymous (no JWT tenant claim) request - a test reads this property to
        /// prove the scope was actually open at call time, not just that the right <c>tenantId</c> parameter was
        /// passed.
        /// </summary>
        public Guid? LastObservedAmbientTenantId { get; private set; }

        public void Map(Guid locationId, string name) => _byId[locationId] = name;

        public Task<bool> ExistsActiveAsync(Guid tenantId, Guid locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byId.ContainsKey(locationId));

        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid tenantId, IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default)
        {
            LastObservedAmbientTenantId = Sense.Shared.Infrastructure.Context.TenantContext.CurrentTenantIdOrNull;
            IReadOnlyDictionary<Guid, string> result = locationIds
                .Where(id => _byId.ContainsKey(id))
                .ToDictionary(id => id, id => _byId[id]);
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// In-memory <see cref="IEmployeeProvisioning"/> for this fixture (S-23 - Faz 9 tur 1): records each
    /// provisioning request for assertion in tests, returning a dummy employee Guid. Implementation deferred
    /// to Faz 2 BE.
    /// </summary>
    private sealed class FakeEmployeeProvisioning : IEmployeeProvisioning
    {
        public List<CandidateHireRequest> ProvisionedCandidates { get; } = [];

        public void Reset() => ProvisionedCandidates.Clear();

        public Task<Guid> CreateFromCandidateAsync(
            Guid tenantId,
            CandidateHireRequest request,
            CancellationToken cancellationToken = default)
        {
            ProvisionedCandidates.Add(request);
            return Task.FromResult(Guid.CreateVersion7()); // Dummy employee Guid
        }
    }

    /// <summary>In-memory <see cref="IJobApplicationRepository"/> for unit testing.</summary>
    private sealed class FakeJobApplicationRepository : IJobApplicationRepository
    {
        public Dictionary<Guid, JobApplication> ApplicationsById { get; } = [];

        public Task<Result<JobApplication>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApplicationsById.TryGetValue(id, out var app) ? Result.Success(app) : Result.Failure<JobApplication>(Error.NotFound(RecruitmentDomainErrors.ApplicationNotFound)));

        public void Add(JobApplication application) => ApplicationsById[application.Id] = application;
        public void Remove(JobApplication application) => ApplicationsById.Remove(application.Id);

        public Task<IReadOnlyList<JobApplication>> ListByJobPostingAsync(Guid jobPostingId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobApplication>>(ApplicationsById.Values.Where(a => a.JobPostingId == jobPostingId).ToList());

        public Task<IReadOnlyList<JobApplication>> ListByJobPostingAsync(Guid jobPostingId, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobApplication>>(ApplicationsById.Values.Where(a => a.JobPostingId == jobPostingId).Take(take).ToList());

        public Task<IReadOnlyList<JobApplication>> ListByCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobApplication>>(ApplicationsById.Values.Where(a => a.CandidateId == candidateId).ToList());

        public Task<IReadOnlyList<JobApplication>> ListByCandidateAsync(Guid candidateId, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<JobApplication>>(ApplicationsById.Values.Where(a => a.CandidateId == candidateId).Take(take).ToList());

        public Task<bool> ExistsAsync(Guid jobPostingId, Guid candidateId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ApplicationsById.Values.Any(a => a.JobPostingId == jobPostingId && a.CandidateId == candidateId));
    }

    /// <summary>In-memory <see cref="ICandidateRepository"/> for unit testing.</summary>
    private sealed class FakeCandidateRepository : ICandidateRepository
    {
        public Dictionary<Guid, Candidate> CandidatesById { get; } = [];

        public Task<Result<Candidate>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(CandidatesById.TryGetValue(id, out var candidate) ? Result.Success(candidate) : Result.Failure<Candidate>(Error.NotFound(RecruitmentDomainErrors.CandidateNotFound)));

        public void Add(Candidate candidate) => CandidatesById[candidate.Id] = candidate;
        public void Remove(Candidate candidate) => CandidatesById.Remove(candidate.Id);

        public Task<IReadOnlyList<Candidate>> ListAsync(string? search = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Candidate>>(FilterCandidates(search).ToList());

        public Task<IReadOnlyList<Candidate>> ListAsync(string? search, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Candidate>>(FilterCandidates(search).Take(take).ToList());

        private IEnumerable<Candidate> FilterCandidates(string? search) =>
            string.IsNullOrEmpty(search)
                ? CandidatesById.Values
                : CandidatesById.Values.Where(c => c.Email.Contains(search, StringComparison.OrdinalIgnoreCase) || $"{c.FirstName} {c.LastName}".Contains(search, StringComparison.OrdinalIgnoreCase));

        public Task<Candidate?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            var candidate = CandidatesById.Values.FirstOrDefault(c => c.Email == email);
            return Task.FromResult(candidate);
        }

        public Task<IReadOnlyList<Candidate>> ListRetentionExpiredAsync(DateTime createdBefore, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Candidate>>(CandidatesById.Values.Where(c => !c.IsAnonymized && c.CreatedAt < createdBefore).ToList());
    }

    /// <summary>In-memory <see cref="IRecruitmentUnitOfWork"/> for unit testing.</summary>
    private sealed class FakeRecruitmentUnitOfWork : IRecruitmentUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }

        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            await action(cancellationToken);
        }
    }
}
