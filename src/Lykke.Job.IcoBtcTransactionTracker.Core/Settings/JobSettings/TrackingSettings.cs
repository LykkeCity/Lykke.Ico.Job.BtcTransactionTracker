using System;
using System.Collections.Generic;
using System.Text;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings
{
    public class TrackingSettings
    {
        public UInt16 ConfirmationLimit { get; set; }
        public String NinjaNetwork { get; set; }
        public String NinjaUrl { get; set; }
        public UInt64 StartHeight { get; set; }
    }
}
