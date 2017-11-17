using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AzureStorage;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.ProcessedBlocks;

namespace Lykke.Job.IcoBtcTransactionTracker.AzureRepositories
{
    public class ProcessedBlockRepository : IProcessedBlockRepository
    {
        private INoSQLTableStorage<ProcessedBlock> _tableStorage;

        public ProcessedBlockRepository(INoSQLTableStorage<ProcessedBlock> tableStorage)
        {
            _tableStorage = tableStorage;
        }

        public async Task<IProcessedBlock> GetLastProcessedBlockAsync()
        {
            var lastBlocks = await _tableStorage.GetDataAsync(ProcessedBlock.PK_LAST);

            Debug.Assert(lastBlocks.Count() < 2);

            return lastBlocks.FirstOrDefault();
        }
    }
}
