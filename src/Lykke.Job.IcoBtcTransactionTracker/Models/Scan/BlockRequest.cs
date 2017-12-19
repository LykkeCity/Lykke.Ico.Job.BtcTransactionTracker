using System;
using System.Collections.Generic;
using System.Text;

namespace Lykke.Job.IcoBtcTransactionTracker.Models.Scan
{
    public class BlockRequest
    {
        public ulong? Height { get; set; }
        public string Id { get; set; }
    }
}
