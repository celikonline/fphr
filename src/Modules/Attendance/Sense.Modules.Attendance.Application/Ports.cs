using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Attendance.Application;

/// <summary>Attendance DbContext's unit of work, for integration event handlers that run outside the command pipeline.</summary>
public interface IAttendanceUnitOfWork : IUnitOfWork;
