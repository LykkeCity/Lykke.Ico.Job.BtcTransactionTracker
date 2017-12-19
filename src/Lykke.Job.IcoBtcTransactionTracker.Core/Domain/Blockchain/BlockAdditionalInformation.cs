using System;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain
{
    public class BlockAdditionalInformation
    {
        public string BlockId { get; set; }
        public DateTimeOffset BlockTime { get; set; }
        public ulong Height { get; set; }
        public ulong Confirmations { get; set; }
    }
}
