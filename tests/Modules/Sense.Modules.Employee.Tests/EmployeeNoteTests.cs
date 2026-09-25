using System.Reflection;
using NSubstitute;
using Sense.Modules.Employee.Application.Notes;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Microsoft.Extensions.Logging.Abstractions;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Infrastructure.Messaging.Behaviours;
using Sense.Shared.Kernel.Results;
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
        var employees = Substitute.For<IEmployeeReadStore>();
        employees.GetAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(new EmployeeRow(
            EmployeeId, "A", "B", "A B", null, null, null, null, null, null, EmployeeStatus.Active, null, null, null));
        var notes = Substitute.For<IEmployeeNoteRepository>();
        var handler = new CreateEmployeeNoteHandler(employees, notes, Access(), user);

        var result = await handler.Handle(new CreateEmployeeNoteCommand(EmployeeId, "T", "C", EmployeeNoteImportance.High), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AuthorUserId.ShouldBe(userId);
        result.Value.AuthorName.ShouldBe("Ayşe İK");
        notes.Received(1).Add(Arg.Is<EmployeeNote>(n => n.AuthorUserId == userId && n.TenantId == TenantId));
    }

    private static IFileStorage StorageReturning(long size)
    {
        var storage = Substitute.For<IFileStorage>();
        storage.InitiateUploadAsync(TenantId, EmployeeNote.BucketName, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PresignedUrlRequest("https://up", "req", DateTime.UtcNow.AddMinutes(5), new Dictionary<string, string>()));
        storage.ConfirmUploadAsync(TenantId, EmployeeNote.BucketName, Arg.Any<string>(), "etag", Arg.Any<CancellationToken>()).Returns(size);
        storage.GetDownloadUrlAsync(TenantId, EmployeeNote.BucketName, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PresignedUrlResponse("https://down", DateTime.UtcNow.AddMinutes(15)));
        return storage;
    }

    private static (EmployeeNote Note, IEmployeeNoteRepository Notes) NoteInRepository()
    {
        var note = NewNote();
        var notes = Substitute.For<IEmployeeNoteRepository>();
        notes.GetTrackedAsync(note.Id, Arg.Any<CancellationToken>()).Returns(note);
        return (note, notes);
    }

    private static Task<Result<InitiateNoteAttachmentUploadResponse>> Initiate(
        EmployeeNote note, IEmployeeNoteRepository notes, EmployeeNoteAccess access, IFileStorage storage, string name = "rapor.pdf", string type = "application/pdf") =>
        new InitiateNoteAttachmentUploadHandler(notes, access, storage)
            .Handle(new InitiateNoteAttachmentUploadCommand(note.Id, name, type, 1234), CancellationToken.None);

    private static Task<Result<EmployeeNoteDto>> Confirm(EmployeeNote note, IEmployeeNoteRepository notes, EmployeeNoteAccess access, IFileStorage storage) =>
        new ConfirmNoteAttachmentUploadHandler(notes, access, storage, NullLogger<ConfirmNoteAttachmentUploadHandler>.Instance)
            .Handle(new ConfirmNoteAttachmentUploadCommand(note.Id, "etag"), CancellationToken.None);

    [Fact]
    public async Task Attachment_upload_confirm_flow()
    {
        var storage = StorageReturning(1234);
        var (note, notes) = NoteInRepository();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var access = Access(storage, outbox);

        var initiated = await Initiate(note, notes, access, storage, "..\\evil/rapor.pdf");

        initiated.IsSuccess.ShouldBeTrue();
        initiated.Value.UploadUrl.ShouldBe("https://up");
        note.PendingAttachmentFileName.ShouldBe("rapor.pdf");
        note.HasConfirmedAttachment.ShouldBeFalse();
        (await access.ToDtoAsync(note, CancellationToken.None)).Attachment.ShouldBeNull();

        var confirmed = await Confirm(note, notes, access, storage);

        confirmed.IsSuccess.ShouldBeTrue();
        confirmed.Value.Attachment!.SizeBytes.ShouldBe(1234);
        confirmed.Value.Attachment.DownloadUrl.ShouldBe("https://down");
        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());

        (await new DeleteNoteAttachmentHandler(notes, access).Handle(new DeleteNoteAttachmentCommand(note.Id), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        note.HasConfirmedAttachment.ShouldBeFalse();
        outbox.Received(1).Enqueue(Arg.Is<IIntegrationEvent>(e => e is EmployeeNoteFileOrphaned));
    }

    [Fact]
    public async Task New_upload_keeps_the_old_file_until_confirmed_then_queues_its_deletion_after_commit()
    {
        var storage = StorageReturning(1234);
        var (note, notes) = NoteInRepository();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var access = Access(storage, outbox);
        await Initiate(note, notes, access, storage);
        await Confirm(note, notes, access, storage);
        var oldKey = note.AttachmentStorageKey!;

        await Initiate(note, notes, access, storage, "yeni.pdf");

        note.AttachmentStorageKey.ShouldBe(oldKey);
        note.HasConfirmedAttachment.ShouldBeTrue();
        await storage.DidNotReceive().DeleteFileAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());

        await Confirm(note, notes, access, storage);

        note.AttachmentFileName.ShouldBe("yeni.pdf");
        outbox.Received(1).Enqueue(Arg.Is<IIntegrationEvent>(e => e is EmployeeNoteFileOrphaned && ((EmployeeNoteFileOrphaned)e).StorageKey == oldKey));
        await storage.DidNotReceive().DeleteFileAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cleanup_handler_deletes_the_object_and_swallows_storage_failures()
    {
        var storage = Substitute.For<IFileStorage>();
        storage.DeleteFileAsync(TenantId, EmployeeNote.BucketName, "k", Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("down"));
        var handler = new EmployeeNoteFileCleanupHandler(storage, NullLogger<EmployeeNoteFileCleanupHandler>.Instance);

        await handler.Handle(new EmployeeNoteFileOrphaned(TenantId, "k"), CancellationToken.None);

        await storage.Received(1).DeleteFileAsync(TenantId, EmployeeNote.BucketName, "k", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("a.exe", "application/octet-stream")]
    [InlineData("a.pdf", "text/html")]
    [InlineData("a.png", "application/pdf")]
    [InlineData("a", "application/pdf")]
    [InlineData("a.svg", "image/svg+xml")]
    public async Task Disallowed_attachment_types_are_rejected(string name, string type)
    {
        var storage = StorageReturning(1);
        var (note, notes) = NoteInRepository();

        var result = await Initiate(note, notes, Access(storage), storage, name, type);

        result.Error.Code.ShouldBe(EmployeeNoteErrors.AttachmentTypeNotAllowed);
        await storage.DidNotReceive().InitiateUploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("a.pdf", "application/pdf")]
    [InlineData("a.PNG", "image/png")]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.txt", "text/plain")]
    [InlineData("a.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("a.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public void Allowed_attachment_types_pass(string name, string type) => EmployeeNoteUploadPolicy.IsAllowed(name, type).ShouldBeTrue();

    [Fact]
    public async Task Attachment_over_10_MB_is_rejected_at_initiate()
    {
        var storage = StorageReturning(1);
        var (note, notes) = NoteInRepository();

        var result = await new InitiateNoteAttachmentUploadHandler(notes, Access(storage), storage)
            .Handle(new InitiateNoteAttachmentUploadCommand(note.Id, "a.pdf", "application/pdf", EmployeeNoteLimits.AttachmentMaxBytes + 1), CancellationToken.None);

        result.Error.Code.ShouldBe(EmployeeNoteErrors.AttachmentTooLarge);
    }

    [Fact]
    public async Task Confirm_with_oversized_real_size_deletes_the_object_and_fails()
    {
        var storage = StorageReturning(EmployeeNoteLimits.AttachmentMaxBytes + 1);
        var (note, notes) = NoteInRepository();
        var access = Access(storage);
        await Initiate(note, notes, access, storage);
        var pendingKey = note.PendingAttachmentStorageKey!;

        var result = await Confirm(note, notes, access, storage);

        result.Error.Code.ShouldBe(EmployeeNoteErrors.AttachmentTooLarge);
        note.HasConfirmedAttachment.ShouldBeFalse();
        await storage.Received(1).DeleteFileAsync(TenantId, EmployeeNote.BucketName, pendingKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_without_a_user_id_fails_instead_of_writing_an_empty_author()
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns((Guid?)null);
        var employees = Substitute.For<IEmployeeReadStore>();
        employees.GetAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(new EmployeeRow(
            EmployeeId, "A", "B", "A B", null, null, null, null, null, null, EmployeeStatus.Active, null, null, null));
        var notes = Substitute.For<IEmployeeNoteRepository>();

        var result = await new CreateEmployeeNoteHandler(employees, notes, Access(), user)
            .Handle(new CreateEmployeeNoteCommand(EmployeeId, "T", "C", EmployeeNoteImportance.Low), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        notes.DidNotReceive().Add(Arg.Any<EmployeeNote>());
    }

    [Fact]
    public async Task A_caller_holding_only_my_permissions_cannot_read_or_write_notes()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(Guid.NewGuid());
        user.Roles.Returns(new HashSet<string>());
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<string>(1).Contains(".my.", StringComparison.Ordinal));
        var scope = Substitute.For<IDataScopeService>();
        scope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var behaviour = new AuthorizationBehaviour<ListEmployeeNotesQuery, Result<Sense.Shared.Contracts.Paging.PagedResult<EmployeeNoteDto>>>(user, permissions, scope);
        var reached = false;

        var result = await behaviour.Handle(
            new ListEmployeeNotesQuery(EmployeeId, null),
            () =>
            {
                reached = true;
                return Task.FromResult(Result.Success(new Sense.Shared.Contracts.Paging.PagedResult<EmployeeNoteDto>([], 1, 20, 0)));
            },
            CancellationToken.None);

        reached.ShouldBeFalse();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Out_of_scope_note_is_reported_as_not_found()
    {
        var note = NewNote();
        var notes = Substitute.For<IEmployeeNoteRepository>();
        notes.GetTrackedAsync(note.Id, Arg.Any<CancellationToken>()).Returns(note);
        var scope = Substitute.For<IDataScopeService>();
        scope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var access = new EmployeeNoteAccess(TenantContext(), Substitute.For<IFileStorage>(), scope, Substitute.For<IIntegrationEventOutbox>());

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

    private static EmployeeNoteAccess Access(IFileStorage? storage = null, IIntegrationEventOutbox? outbox = null)
    {
        var scope = Substitute.For<IDataScopeService>();
        scope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        return new EmployeeNoteAccess(TenantContext(), storage ?? Substitute.For<IFileStorage>(), scope, outbox ?? Substitute.For<IIntegrationEventOutbox>());
    }
}
