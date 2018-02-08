namespace Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings
{
    public class IcoBtcTransactionTrackerSettings
    {
        public DbSettings Db { get; set; }
        public TrackingSettings Tracking { get; set; }
        public int TrackingInterval { get; set; }
        public string CommonServiceUrl { get; set; }
    }
}
