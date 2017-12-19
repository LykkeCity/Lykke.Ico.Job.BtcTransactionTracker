using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Lykke.Job.IcoBtcTransactionTracker.Models.Scan
{
    public class RangeRequest
    {
        public ulong FromHeight { get; set; }
        public ulong ToHeight { get; set; }
    }
}
