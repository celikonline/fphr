using NSubstitute;
using Sense.Modules.Announcements.Application.Recognitions;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public class RecognitionCommandsTests
{
    [Fact]
    public async Task SendRecognition_WithValidCommand_ShouldSucceed()
    {
        var tenantId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(senderId);
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        employeeLookup.ExistsActiveAsync(tenantId, receiverId, Arg.Any<CancellationToken>()).Returns(true);
        var recognitions = Substitute.For<IRecognitionRepository>();

        var handler = new SendRecognitionHandler(tenant, currentUser, employeeLookup, recognitions);
        var result = await handler.Handle(new SendRecognitionCommand(receiverId, "Harika iş çıkardın, teşekkürler!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        recognitions.Received(1).Add(Arg.Is<Recognition>(r =>
            r.TenantId == tenantId && r.SenderEmployeeId == senderId && r.ReceiverEmployeeId == receiverId
            && r.Message == "Harika iş çıkardın, teşekkürler!"));
    }

    [Fact]
    public async Task SendRecognition_WithEmptyMessage_ShouldFail()
    {
        var tenantId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(senderId);
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        employeeLookup.ExistsActiveAsync(tenantId, receiverId, Arg.Any<CancellationToken>()).Returns(true);
        var recognitions = Substitute.For<IRecognitionRepository>();

        var handler = new SendRecognitionHandler(tenant, currentUser, employeeLookup, recognitions);
        var result = await handler.Handle(new SendRecognitionCommand(receiverId, "   "), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RecognitionDomainErrors.MessageRequired, result.Error.Code);
        recognitions.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task SendRecognition_WithMessageLongerThanLimit_ShouldFail()
    {
        var tenantId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(senderId);
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        employeeLookup.ExistsActiveAsync(tenantId, receiverId, Arg.Any<CancellationToken>()).Returns(true);
        var recognitions = Substitute.For<IRecognitionRepository>();

        var handler = new SendRecognitionHandler(tenant, currentUser, employeeLookup, recognitions);
        var tooLong = new string('x', AnnouncementsLimits.RecognitionMessageMaxLength + 1);
        var result = await handler.Handle(new SendRecognitionCommand(receiverId, tooLong), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RecognitionDomainErrors.MessageRequired, result.Error.Code);
        recognitions.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task SendRecognition_MessageAtExactLimit_ShouldSucceed()
    {
        var tenantId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(senderId);
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        employeeLookup.ExistsActiveAsync(tenantId, receiverId, Arg.Any<CancellationToken>()).Returns(true);
        var recognitions = Substitute.For<IRecognitionRepository>();

        var handler = new SendRecognitionHandler(tenant, currentUser, employeeLookup, recognitions);
        var atLimit = new string('x', AnnouncementsLimits.RecognitionMessageMaxLength);
        var result = await handler.Handle(new SendRecognitionCommand(receiverId, atLimit), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SendRecognition_ToSelf_ShouldFail()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(employeeId);
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        employeeLookup.ExistsActiveAsync(tenantId, employeeId, Arg.Any<CancellationToken>()).Returns(true);
        var recognitions = Substitute.For<IRecognitionRepository>();

        var handler = new SendRecognitionHandler(tenant, currentUser, employeeLookup, recognitions);
        var result = await handler.Handle(new SendRecognitionCommand(employeeId, "Kendime teşekkür ederim"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RecognitionDomainErrors.CannotRecognizeSelf, result.Error.Code);
        recognitions.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task SendRecognition_WhenReceiverDoesNotExist_ShouldFail()
    {
        var tenantId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(senderId);
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        employeeLookup.ExistsActiveAsync(tenantId, receiverId, Arg.Any<CancellationToken>()).Returns(false);
        var recognitions = Substitute.For<IRecognitionRepository>();

        var handler = new SendRecognitionHandler(tenant, currentUser, employeeLookup, recognitions);
        var result = await handler.Handle(new SendRecognitionCommand(receiverId, "Teşekkürler"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RecognitionDomainErrors.ReceiverNotFound, result.Error.Code);
        recognitions.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task SendRecognition_WhenCallerHasNoEmployeeRecord_ShouldFail()
    {
        var tenantId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns((Guid?)null);
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        var recognitions = Substitute.For<IRecognitionRepository>();

        var handler = new SendRecognitionHandler(tenant, currentUser, employeeLookup, recognitions);
        var result = await handler.Handle(new SendRecognitionCommand(receiverId, "Teşekkürler"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RecognitionDomainErrors.SenderNotFound, result.Error.Code);
        recognitions.DidNotReceiveWithAnyArgs().Add(default!);
    }
}

public class RecognitionValidatorTests
{
    [Fact]
    public void ValidCommand_Passes() =>
        Assert.True(new SendRecognitionValidator().Validate(new SendRecognitionCommand(Guid.NewGuid(), "Teşekkürler")).IsValid);

    [Fact]
    public void EmptyMessage_IsRejected() =>
        Assert.False(new SendRecognitionValidator().Validate(new SendRecognitionCommand(Guid.NewGuid(), "")).IsValid);

    [Fact]
    public void EmptyReceiverId_IsRejected() =>
        Assert.False(new SendRecognitionValidator().Validate(new SendRecognitionCommand(Guid.Empty, "Teşekkürler")).IsValid);

    [Fact]
    public void MessageLongerThanLimit_IsRejected() =>
        Assert.False(new SendRecognitionValidator().Validate(
            new SendRecognitionCommand(Guid.NewGuid(), new string('x', AnnouncementsLimits.RecognitionMessageMaxLength + 1))).IsValid);
}
