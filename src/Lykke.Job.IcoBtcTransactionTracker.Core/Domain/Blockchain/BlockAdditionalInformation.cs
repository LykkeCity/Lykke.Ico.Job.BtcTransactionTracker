using System;
using System.Collections.Generic;
using System.Text;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain
{
    public class BlockAdditionalInformation
    {
        public String BlockId { get; set; }
        public DateTimeOffset BlockTime { get; set; }
        public UInt64 Height { get; set; }
        public UInt64 Confirmations { get; set; }
    }
}
