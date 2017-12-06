using System;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings
{
    public class TrackingSettings
    {
        public UInt16 ConfirmationLimit { get; set; }
        public String BtcUrl { get; set; }
        public String BtcNetwork { get; set; }
        public String BtcTrackerUrl { get; set; }
        public UInt64 StartHeight { get; set; }
    }
}
