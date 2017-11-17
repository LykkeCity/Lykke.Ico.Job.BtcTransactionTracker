using System;
using System.Collections.Generic;
using System.Text;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings
{
    public class TrackingSettings
    {
        public int ConfirmationLimit { get; set; }
        public string NinjaNetwork { get; set; }
        public string NinjaUrl { get; set; }
        public int StartHeight { get; set; }
    }
}
