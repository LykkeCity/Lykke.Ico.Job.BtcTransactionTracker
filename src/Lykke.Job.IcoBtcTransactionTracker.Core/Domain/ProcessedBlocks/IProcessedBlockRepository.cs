using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Domain.ProcessedBlocks
{
    public interface IProcessedBlockRepository
    {
        Task<IProcessedBlock> GetLastProcessedBlockAsync();
    }
}
