using Application.Shared.Services.Abstract;
using Domain.NotificationManagement.Enums;
using Domain.NotificationManagement.Repositories;
using Domain.Shared;
using Microsoft.Extensions.Logging;

namespace Application.Shared.Services.Concrete
{
    /// <summary>
    /// Notification Service
    /// </summary>
    /// <typeparam name="TRequest">TRequest.</typeparam>
    /// <typeparam name="TResponse">TResponse.</typeparam>
    public class NotificationService<TRequest, TResponse> : INotificationService<TRequest, TResponse>
        where TRequest : INotificationRequest
        where TResponse : INotificationResponse
    {
        private readonly INotificationRepository notificationRepository;
        private readonly INotificationSender<TRequest, TResponse> notificationSender;
        private readonly ILogger<NotificationService<TRequest, TResponse>> logger;
        private readonly IUnitOfWork unitOfWork;

        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationService{TRequest, TResponse}"/> class.
        /// </summary>
        /// <param name="notificationRepository">notificationRepository.</param>
        /// <param name="notificationSender">notificationSender.</param>
        /// <param name="logger">logger.</param>
        /// <param name="unitOfWork">unitOfWork.</param>
        public NotificationService(
            INotificationRepository notificationRepository,
            INotificationSender<TRequest, TResponse> notificationSender,
            ILogger<NotificationService<TRequest, TResponse>> logger,
            IUnitOfWork unitOfWork)
        {
            this.notificationRepository = notificationRepository;
            this.notificationSender = notificationSender;
            this.logger = logger;
            this.unitOfWork = unitOfWork;
        }

        /// <summary>
        /// ProcessPendingNotificationsAsync
        /// </summary>
        /// <returns>Task</returns>
        public async Task ProcessPendingNotificationsAsync()
        {
            var notifications = this.notificationRepository.Query(x => x.Status == NotificationStatus.Pending).Take(100).ToList();

            foreach (var notification in notifications)
            {
                try
                {
                    var request = this.notificationSender.NotificationToRequest(notification);

                    var response = await this.notificationSender.SendNotificationAsync(request);

                    if (response.IsSuccess)
                    {
                        notification.SentSuccess();
                    }
                    else
                    {
                        notification.SentFailed();
                    }
                }
                catch (Exception ex)
                {
                    var message = $"Process pending notification failed: {ex.Message}";

                    this.logger.LogError(message, ex);

                    notification.SentFailed(message);
                }
                finally
                {
                    await this.unitOfWork.SaveAsync();
                }
            }
        }
    }
}
