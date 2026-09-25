using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Infrastructure.Jobs;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Messaging;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Recalculation;

public sealed class ShiftPeriodAutoLockJobHandlerTests
{
    [Fact]
    public async Task AutoLock_handler_dispatches_LockDueShiftPeriodsCommand()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Send(Arg.Any<ICommand<bool>>(), Arg.Any<CancellationToken>()).Returns(Sense.Shared.Kernel.Results.Result.Success(true));
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero));
        var handler = new ShiftPeriodAutoLockJobHandler(dispatcher, clock, NullLogger<ShiftPeriodAutoLockJobHandler>.Instance);

        handler.Handles.ShouldBe(JobType.ShiftPeriodAutoLock);
        await handler.ExecuteAsync(new ScheduledJobContext(Guid.NewGuid(), null, DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);

        await dispatcher.Received(1).Send(
            Arg.Is<ICommand<bool>>(c => c is LockDueShiftPeriodsCommand && ((LockDueShiftPeriodsCommand)c).Today == new DateOnly(2026, 10, 3)),
            Arg.Any<CancellationToken>());
    }
}
