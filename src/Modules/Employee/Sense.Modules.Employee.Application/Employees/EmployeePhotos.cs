using System.Security.Cryptography;
using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>Photo bytes ready to be served; <see cref="ETag"/> is a quoted content hash for conditional requests.</summary>
public sealed record EmployeePhotoFile(string ContentType, byte[] Content, string ETag);

/// <summary>Raster image sniffing: the client-declared content type is never trusted, only the file signature.</summary>
public static class EmployeePhotoImage
{
    private const int WebpHeaderLength = 12;
    private const int ETagHexLength = 16;

    /// <returns>image/jpeg, image/png or image/webp; null when the bytes are none of them.</returns>
    public static string? DetectContentType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length >= png.Length && data[..png.Length].SequenceEqual(png))
        {
            return "image/png";
        }

        if (data.Length >= WebpHeaderLength && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    public static string ComputeETag(byte[] content) =>
        string.Concat("\"", Convert.ToHexString(SHA256.HashData(content))[..ETagHexLength], "\"");
}

/// <summary>HR path: sets/replaces the photo of any employee (out-of-scope targets return 404, see IScopedRequest).</summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record SetEmployeePhotoCommand(Guid EmployeeId, byte[] Content) : ICommand, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

/// <summary>Self-service path: sets/replaces the caller's own photo.</summary>
[RequiresPermission(EmployeePermissions.ProfileSelfEditKey)]
public sealed record SetMyPhotoCommand(byte[] Content) : ICommand;

[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record DeleteEmployeePhotoCommand(Guid EmployeeId) : ICommand, IScopedRequest
{
    public Guid? TargetEmployeeId => EmployeeId;
}

[RequiresPermission(EmployeePermissions.ProfileSelfEditKey)]
public sealed record DeleteMyPhotoCommand : ICommand;

/// <summary>Any employee's photo, for callers who may see the company directory (photos are part of the directory cards).</summary>
[RequiresPermission(EmployeePermissions.DirectoryViewKey)]
public sealed record GetEmployeePhotoQuery(Guid EmployeeId) : IQuery<EmployeePhotoFile>;

[RequiresPermission(EmployeePermissions.MyViewKey)]
public sealed record GetMyPhotoQuery : IQuery<EmployeePhotoFile>;

/// <summary>Shared write path for both the HR and the self-service commands.</summary>
public sealed class EmployeePhotoWriter(ITenantContext tenant, IEmployeePhotoRepository photos)
{
    public async Task<Result> SaveAsync(Guid employeeId, byte[] content, CancellationToken cancellationToken)
    {
        if (content.Length == 0)
        {
            return Error.Validation(EmployeeDomainErrors.PhotoRequired);
        }

        if (content.Length > EmployeeLimits.PhotoMaxBytes)
        {
            return Error.Rule(EmployeeDomainErrors.PhotoTooLarge);
        }

        var contentType = EmployeePhotoImage.DetectContentType(content);
        if (contentType is null)
        {
            return Error.Validation(EmployeeDomainErrors.PhotoTypeUnsupported);
        }

        var existing = await photos.GetTrackedAsync(employeeId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            photos.Add(EmployeePhoto.Create(tenant.TenantId, employeeId, contentType, content));
        }
        else
        {
            existing.Replace(contentType, content);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var existing = await photos.GetTrackedAsync(employeeId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            photos.Remove(existing);
        }

        return Result.Success();
    }
}

public sealed class SetEmployeePhotoHandler(IEmployeeReadStore store, EmployeePhotoWriter writer) : ICommandHandler<SetEmployeePhotoCommand>
{
    public async Task<Result> Handle(SetEmployeePhotoCommand command, CancellationToken cancellationToken)
    {
        if (await store.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        return await writer.SaveAsync(command.EmployeeId, command.Content, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class SetMyPhotoHandler(SelfEmployeeResolver self, EmployeePhotoWriter writer) : ICommandHandler<SetMyPhotoCommand>
{
    public async Task<Result> Handle(SetMyPhotoCommand command, CancellationToken cancellationToken)
    {
        var employeeId = await self.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (employeeId is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        return await writer.SaveAsync(employeeId.Value, command.Content, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class DeleteEmployeePhotoHandler(IEmployeeReadStore store, EmployeePhotoWriter writer) : ICommandHandler<DeleteEmployeePhotoCommand>
{
    public async Task<Result> Handle(DeleteEmployeePhotoCommand command, CancellationToken cancellationToken)
    {
        if (await store.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        return await writer.DeleteAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class DeleteMyPhotoHandler(SelfEmployeeResolver self, EmployeePhotoWriter writer) : ICommandHandler<DeleteMyPhotoCommand>
{
    public async Task<Result> Handle(DeleteMyPhotoCommand command, CancellationToken cancellationToken)
    {
        var employeeId = await self.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (employeeId is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        return await writer.DeleteAsync(employeeId.Value, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class GetEmployeePhotoHandler(IEmployeePhotoRepository photos) : IQueryHandler<GetEmployeePhotoQuery, EmployeePhotoFile>
{
    public async Task<Result<EmployeePhotoFile>> Handle(GetEmployeePhotoQuery query, CancellationToken cancellationToken) =>
        await EmployeePhotoReader.ReadAsync(photos, query.EmployeeId, cancellationToken).ConfigureAwait(false);
}

public sealed class GetMyPhotoHandler(SelfEmployeeResolver self, IEmployeePhotoRepository photos) : IQueryHandler<GetMyPhotoQuery, EmployeePhotoFile>
{
    public async Task<Result<EmployeePhotoFile>> Handle(GetMyPhotoQuery query, CancellationToken cancellationToken)
    {
        var employeeId = await self.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (employeeId is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        return await EmployeePhotoReader.ReadAsync(photos, employeeId.Value, cancellationToken).ConfigureAwait(false);
    }
}

internal static class EmployeePhotoReader
{
    public static async Task<Result<EmployeePhotoFile>> ReadAsync(IEmployeePhotoRepository photos, Guid employeeId, CancellationToken cancellationToken)
    {
        var photo = await photos.GetAsync(employeeId, cancellationToken).ConfigureAwait(false);
        return photo is null
            ? Error.NotFound(EmployeeDomainErrors.PhotoNotFound)
            : new EmployeePhotoFile(photo.ContentType, photo.Content, EmployeePhotoImage.ComputeETag(photo.Content));
    }
}
