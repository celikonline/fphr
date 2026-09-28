using Microsoft.Extensions.Localization;
using NSubstitute;
using Sense.Modules.Employee.Application.Career;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.CardPdf;
using Sense.Modules.Identity.Contracts;
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

    private static CustomFieldValueDto CustomField(string name, CustomFieldDataType dataType, string? value) =>
        new(Guid.NewGuid(), name, name, dataType, CustomFieldSection.Other, [], IsRequired: false, IsEmployeeEditable: false, IsSensitive: false, Value: value);

    private static EmployeeDetailDto DetailWithCustomFields(IReadOnlyList<CustomFieldValueDto> customFields) =>
        new(
            Id: EmployeeId,
            FirstName: "Ali",
            LastName: "Veli",
            DisplayName: "Ali Veli",
            NationalId: null,
            WorkEmail: null,
            PersonalEmail: null,
            WorkPhone: null,
            HireDate: null,
            BirthDate: null,
            Status: "active",
            OrgUnitId: null,
            ManagerEmployeeId: null,
            ManagerDisplayName: null,
            UserId: null,
            AccountStatus: UserAccountStatus.None,
            AccessType: null,
            LastInvitationSentAt: null,
            Gender: Gender.Unspecified,
            MaritalStatus: MaritalStatus.Unspecified,
            ChildCount: null,
            EducationStatus: null,
            HighestEducationLevel: null,
            MilitaryStatus: null,
            DisabilityDegree: null,
            BloodType: null,
            MobilePhone: null,
            HomePhone: null,
            AddressLine: null,
            City: null,
            PostalCode: null,
            ContractType: null,
            EmploymentType: null,
            SuspensionReason: null,
            CustomFields: customFields);

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

    [Fact]
    public async Task Account_TranslatesAccessType()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var detail = DetailWithCustomFields([]) with { AccessType = "Employee" };
        dispatcher.Query(Arg.Any<GetEmployeeQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(detail)));
        var localizer = Substitute.For<IStringLocalizer<SharedResource>>();
        localizer[Arg.Any<string>()].Returns(call => new LocalizedString(call.Arg<string>(), call.Arg<string>(), resourceNotFound: true));
        localizer["report.enum.AccessType.Employee"]
            .Returns(new LocalizedString("report.enum.AccessType.Employee", "Çalışan", resourceNotFound: false));

        var result = await new AccountCardPdfSection(dispatcher, localizer).BuildAsync(AdminRequest, CancellationToken.None);

        var row = result.Blocks!.OfType<SimplePdfKeyValues>().ShouldHaveSingleItem().Rows.Single(r => r.Key == "Erişim tipi");
        row.Value.ShouldBe("Çalışan");
    }

    [Fact]
    public async Task CustomFields_Boolean_RendersYesNo()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var detail = DetailWithCustomFields([CustomField("Sürücü belgesi var mı", CustomFieldDataType.Boolean, "true")]);
        dispatcher.Query(Arg.Any<GetEmployeeQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(detail)));

        var result = await new CustomFieldsCardPdfSection(dispatcher, Localizer()).BuildAsync(AdminRequest, CancellationToken.None);

        var row = result.Blocks!.OfType<SimplePdfKeyValues>().ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        row.Key.ShouldBe("Sürücü belgesi var mı");
        row.Value.ShouldBe("Evet");
    }

    [Fact]
    public async Task CustomFields_MultiSelect_JoinsOptionsWithComma()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var detail = DetailWithCustomFields([CustomField("Yetkinlikler", CustomFieldDataType.MultiSelect, "[\"A\",\"B\"]")]);
        dispatcher.Query(Arg.Any<GetEmployeeQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(detail)));

        var result = await new CustomFieldsCardPdfSection(dispatcher, Localizer()).BuildAsync(AdminRequest, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfKeyValues>().ShouldHaveSingleItem().Rows.ShouldHaveSingleItem().Value.ShouldBe("A, B");
    }

    [Fact]
    public async Task CustomFields_Date_RendersTurkishDate()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var detail = DetailWithCustomFields([CustomField("Sözleşme tarihi", CustomFieldDataType.Date, "2026-03-01")]);
        dispatcher.Query(Arg.Any<GetEmployeeQuery>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(detail)));

        var result = await new CustomFieldsCardPdfSection(dispatcher, Localizer()).BuildAsync(AdminRequest, CancellationToken.None);

        result.Blocks!.OfType<SimplePdfKeyValues>().ShouldHaveSingleItem().Rows.ShouldHaveSingleItem().Value.ShouldBe("01.03.2026");
    }
}
