using System.Text.Json;
using System.Text.Json.Serialization;
using NSubstitute;
using Sense.Modules.Announcements.Application.Support;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

/// <summary>
/// Mobil bulgu: <see cref="SupportTicketDto"/>'nun Category/Channel/Status alanları eskiden
/// <c>enum.ToString()</c> ile dolduruluyordu (PascalCase, "Open"/"LeaveAndOvertime"), bu da API'nin global
/// <see cref="JsonStringEnumConverter"/>'ını (yalnız hâlâ enum tipinde olan alanlara uygulanır) devre dışı
/// bırakıyordu. Web ve mobil camelCase bekliyor (senseik-008 ile aynı desen — bkz.
/// Recruitment.Tests/CandidateDtoSerializationTests.cs). Alanları enum tipinde tutmak dönüştürücünün
/// camelCase üretmesini sağlar; bu test API'nin `WebServiceCollectionExtensions.AddJsonOptions`'ta
/// yapılandırdığı tam <see cref="JsonSerializerOptions"/> şekliyle doğrular.
/// </summary>
public sealed class SupportTicketDtoSerializationTests
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public void CategoryChannelStatus_SerializeCamelCase_NotPascalCase()
    {
        var dto = new SupportTicketDto(
            Guid.NewGuid(), Guid.NewGuid(), "Konu", "Gövde",
            SupportTicketCategory.LeaveAndOvertime, SupportTicketChannel.Ticket, SupportTicketStatus.Open,
            "/leave/requests", null, null, null, DateTime.UtcNow);

        var json = JsonSerializer.Serialize(dto, ApiJsonOptions);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("category").GetString().ShouldBe("leaveAndOvertime");
        document.RootElement.GetProperty("channel").GetString().ShouldBe("ticket");
        document.RootElement.GetProperty("status").GetString().ShouldBe("open");
        json.ShouldNotContain("\"LeaveAndOvertime\"", Case.Sensitive);
        json.ShouldNotContain("\"Open\"", Case.Sensitive);
    }

    [Fact]
    public async Task ListMySupportTickets_KeepsEnumTypes_InsteadOfPreSerializingToString()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ticket = SupportTicket.Create(tenantId, userId, "Konu", "Gövde", SupportTicketCategory.Payroll).Value;

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var tickets = Substitute.For<ISupportTicketRepository>();
        tickets.ListByRequesterAsync(tenantId, userId, Arg.Any<CancellationToken>()).Returns([ticket]);

        var handler = new ListMySupportTicketsHandler(tenant, currentUser, tickets);
        var result = await handler.Handle(new ListMySupportTicketsQuery(), CancellationToken.None);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.Category.ShouldBe(SupportTicketCategory.Payroll);
        dto.Channel.ShouldBe(ticket.Channel);
        dto.Status.ShouldBe(SupportTicketStatus.Open);
    }
}
