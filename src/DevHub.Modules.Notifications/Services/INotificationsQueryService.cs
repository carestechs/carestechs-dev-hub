using DevHub.Modules.Notifications.DTOs;

namespace DevHub.Modules.Notifications.Services;

public interface INotificationsQueryService
{
    Task<IReadOnlyList<PendingActionDto>> ListPendingForMemberAsync(Guid memberId, CancellationToken cancellationToken = default);
}
