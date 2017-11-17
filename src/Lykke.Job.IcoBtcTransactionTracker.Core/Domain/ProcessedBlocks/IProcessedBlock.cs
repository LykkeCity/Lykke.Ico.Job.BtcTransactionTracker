using System;
using System.Collections.Generic;
using System.Text;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Domain.ProcessedBlocks
{
    public interface IProcessedBlock
    {
        int BlockHeight { get; set; }
        string BlockId { get; set; }
    }
}
