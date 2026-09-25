using System.Reflection;
using NSubstitute;
using Sense.Modules.Employee.Application.Notes;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Storage;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>Employee notes (E7): permission gate, length limits, author, attachment flow - no DB.</summary>
public sealed class EmployeeNoteTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static EmployeeNote NewNote() =>
        EmployeeNote.Create(TenantId, EmployeeId, "Başlık", "İçerik", EmployeeNoteImportance.Normal, Guid.NewGuid(), "İK").Value;

    [Fact]
    public void Only_notes_permission_holders_can_read()
    {
        Permission(typeof(ListEmployeeNotesQuery)).ShouldBe("employee.notes.view");
        Permission(typeof(ListEmployeeNotesQuery)).ShouldNotBe(EmployeePermissions.MyViewKey);
        Permission(typeof(ListEmployeeNotesQuery)).ShouldNotBe(EmployeePermissions.EmployeesReadKey);

        var writes = new[]
        {
            typeof(CreateEmployeeNoteCommand), typeof(UpdateEmployeeNoteCommand), typeof(DeleteEmployeeNoteCommand),
            typeof(InitiateNoteAttachmentUploadCommand), typeof(ConfirmNoteAttachmentUploadCommand), typeof(DeleteNoteAttachmentCommand),
        };
        writes.ShouldAllBe(t => Permission(t) == "employee.notes.manage");

        // The permission keys used are the ones the Employee module registers (W0-b).
        EmployeePermissions.All.Select(p => p.Key).ShouldContain(EmployeeNotePermissions.ViewKey);
        EmployeePermissions.All.Select(p => p.Key).ShouldContain(EmployeeNotePermissions.ManageKey);
    }

    [Fact]
    public void Title_max_255_content_max_512()
    {
        EmployeeNote.Create(TenantId, EmployeeId, new string('a', 255), new string('b', 512), EmployeeNoteImportance.Low, Guid.NewGuid(), null)
            .IsSuccess.ShouldBeTrue();
        EmployeeNote.Create(TenantId, EmployeeId, new string('a', 256), "x", EmployeeNoteImportance.Low, Guid.NewGuid(), null)
            .Error.Code.ShouldBe(EmployeeNoteErrors.TitleTooLong);
        EmployeeNote.Create(TenantId, EmployeeId, "t", new string('b', 513), EmployeeNoteImportance.Low, Guid.NewGuid(), null)
            .Error.Code.ShouldBe(EmployeeNoteErrors.ContentTooLong);
        EmployeeNote.Create(TenantId, EmployeeId, "  ", "x", EmployeeNoteImportance.Low, Guid.NewGuid(), null)
            .Error.Code.ShouldBe(EmployeeNoteErrors.TitleRequired);
    }

    [Fact]
    public async Task Author_recorded()
    {
        var userId = Guid.NewGuid();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.DisplayName.Returns("Ayşe İK");
        var employees = Substitute.For<Sense.Modules.Employee.Application.IEmployeeReadStore>();
        employees.GetAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns((Sense.Modules.Employee.Application.EmployeeRow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Sense.Modules.Employee.Application.EmployeeRow)));
        var notes = Substitute.For<IEmployeeNoteRepository>();
        var handler = new CreateEmployeeNoteHandler(employees, notes, Access(), user);

        var result = await handler.Handle(new CreateEmployeeNoteCommand(EmployeeId, "T", "C", EmployeeNoteImportance.High), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AuthorUserId.ShouldBe(userId);
        result.Value.AuthorName.ShouldBe("Ayşe İK");
        notes.Received(1).Add(Arg.Is<EmployeeNote>(n => n.AuthorUserId == userId && n.TenantId == TenantId));
    }

    [Fact]
    public async Task Attachment_upload_confirm_flow()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.InitiateUploadAsync(TenantId, EmployeeNote.BucketName, Arg.Any<string>(), "application/pdf", Arg.Any<CancellationToken>())
            .Returns(new PresignedUrlRequest("https://up", "req", DateTime.UtcNow.AddMinutes(5), new Dictionary<string, string>()));
        storage.ConfirmUploadAsync(TenantId, EmployeeNote.BucketName, Arg.Any<string>(), "etag", Arg.Any<CancellationToken>()).Returns(1234L);
        storage.GetDownloadUrlAsync(TenantId, EmployeeNote.BucketName, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PresignedUrlResponse("https://down", DateTime.UtcNow.AddMinutes(15)));
        var note = NewNote();
        var notes = Substitute.For<IEmployeeNoteRepository>();
        notes.GetTrackedAsync(note.Id, Arg.Any<CancellationToken>()).Returns(note);
        var access = Access(storage);

        var initiated = await new InitiateNoteAttachmentUploadHandler(notes, access, storage)
            .Handle(new InitiateNoteAttachmentUploadCommand(note.Id, "..\\evil/rapor.pdf", "application/pdf", 1234), CancellationToken.None);

        initiated.IsSuccess.ShouldBeTrue();
        initiated.Value.UploadUrl.ShouldBe("https://up");
        note.AttachmentFileName.ShouldBe("rapor.pdf");
        note.HasConfirmedAttachment.ShouldBeFalse();
        (await access.ToDtoAsync(note, CancellationToken.None)).Attachment.ShouldBeNull();

        var confirmed = await new ConfirmNoteAttachmentUploadHandler(notes, access, storage)
            .Handle(new ConfirmNoteAttachmentUploadCommand(note.Id, "etag"), CancellationToken.None);

        confirmed.IsSuccess.ShouldBeTrue();
        confirmed.Value.Attachment!.SizeBytes.ShouldBe(1234);
        confirmed.Value.Attachment.DownloadUrl.ShouldBe("https://down");

        (await new DeleteNoteAttachmentHandler(notes, access).Handle(new DeleteNoteAttachmentCommand(note.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        note.HasConfirmedAttachment.ShouldBeFalse();
        await storage.Received(1).DeleteFileAsync(TenantId, EmployeeNote.BucketName, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Attachment_over_10_MB_is_rejected()
    {
        var storage = Substitute.For<IFileStorage>();
        var note = NewNote();
        var notes = Substitute.For<IEmployeeNoteRepository>();
        notes.GetTrackedAsync(note.Id, Arg.Any<CancellationToken>()).Returns(note);

        var result = await new InitiateNoteAttachmentUploadHandler(notes, Access(storage), storage)
            .Handle(new InitiateNoteAttachmentUploadCommand(note.Id, "a.pdf", "application/pdf", EmployeeNoteLimits.AttachmentMaxBytes + 1), CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeNoteErrors.AttachmentTooLarge);
    }

    [Fact]
    public async Task Out_of_scope_note_is_reported_as_not_found()
    {
        var note = NewNote();
        var notes = Substitute.For<IEmployeeNoteRepository>();
        notes.GetTrackedAsync(note.Id, Arg.Any<CancellationToken>()).Returns(note);
        var scope = Substitute.For<IDataScopeService>();
        scope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var access = new EmployeeNoteAccess(TenantContext(), Substitute.For<IFileStorage>(), scope);

        var result = await new DeleteEmployeeNoteHandler(notes, access).Handle(new DeleteEmployeeNoteCommand(note.Id), CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeNoteErrors.NotFound);
        notes.DidNotReceive().Remove(Arg.Any<EmployeeNote>());
    }

    private static string Permission(Type request) => request.GetCustomAttribute<RequiresPermissionAttribute>()!.Permission;

    private static ITenantContext TenantContext()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static EmployeeNoteAccess Access(IFileStorage? storage = null)
    {
        var scope = Substitute.For<IDataScopeService>();
        scope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        return new EmployeeNoteAccess(TenantContext(), storage ?? Substitute.For<IFileStorage>(), scope);
    }
}
