using Sense.Modules.Attendance.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.WorkSchedules;

/// <summary>One day of the employee's effective work schedule (FG-020 "Takvimim"). <c>BreakMinutes</c> is 0 when the day has no break window.</summary>
public sealed record MyWorkScheduleDayDto(DateOnly Date, bool IsWorkingDay, TimeOnly? Start, TimeOnly? End, int BreakMinutes);

/// <summary>
/// "Takvimim" (FG-020 kabul kriteri 1): vardiyası olmayan çalışanın gün başına sabit çalışma programını döner
/// (Karar 14). Var olan `me/assignments` ucu yalnız atama satırını verir, gün detayını vermez; bu yüzden yeni self uç
/// gerekiyor. Hesap mantığı kopyalanmaz - mevcut <see cref="IWorkScheduleReadService"/> kullanılır. Mobil ekranın
/// gösterdiği pencereyle sınırlı kalmak için aralık en fazla <see cref="MaxRangeDays"/> gün olabilir.
/// </summary>
[RequiresPermission("attendance.my.workschedule")]
public sealed record GetMyWorkScheduleDaysQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<MyWorkScheduleDayDto>>;

public sealed class GetMyWorkScheduleDaysHandler(ICurrentUser currentUser, IWorkScheduleReadService schedules)
    : IQueryHandler<GetMyWorkScheduleDaysQuery, IReadOnlyList<MyWorkScheduleDayDto>>
{
    public const int MaxRangeDays = 42;

    public async Task<Result<IReadOnlyList<MyWorkScheduleDayDto>>> Handle(GetMyWorkScheduleDaysQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        if (query.To < query.From)
        {
            return Error.Validation(AttendanceErrors.DateRangeInvalid);
        }

        if (query.To.DayNumber - query.From.DayNumber + 1 > MaxRangeDays)
        {
            return Error.Validation(AttendanceErrors.WorkScheduleRangeTooLong, ("max", MaxRangeDays));
        }

        var days = new List<MyWorkScheduleDayDto>();
        for (var date = query.From; date <= query.To; date = date.AddDays(1))
        {
            var day = await schedules.GetForDateAsync(employeeId, date, cancellationToken);
            var breakMinutes = day.BreakStart is { } breakStart && day.BreakEnd is { } breakEnd ? (int)(breakEnd - breakStart).TotalMinutes : 0;
            days.Add(new MyWorkScheduleDayDto(date, day.IsWorkingDay, day.Start, day.End, breakMinutes));
        }

        return Result.Success<IReadOnlyList<MyWorkScheduleDayDto>>(days);
    }
}
