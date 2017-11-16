using Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings;
using Lykke.Job.IcoBtcTransactionTracker.Core.Settings.SlackNotifications;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Settings
{
    public class AppSettings
    {
        public IcoBtcTransactionTrackerSettings IcoBtcTransactionTrackerJob { get; set; }
        public SlackNotificationsSettings SlackNotifications { get; set; }
    }
}