using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AzureStorage;
using AzureStorage.Tables;
using Common.Log;
using Lykke.Ico.Core;
using Lykke.Job.IcoBtcTransactionTracker.Core;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.ProcessedBlocks;
using Lykke.SettingsReader;

namespace Lykke.Job.IcoBtcTransactionTracker.AzureRepositories
{
    public class ProcessedBlockRepository : IProcessedBlockRepository
    {
        private readonly INoSQLTableStorage<ProcessedBlockEntity> _tableStorage;
        private static string GetPartitionKey() => Enum.GetName(typeof(CurrencyType), CurrencyType.Bitcoin);
        private static string GetRowKey() => "LAST_PROCESSED";

        public ProcessedBlockRepository(IReloadingManager<string> connectionStringManager, ILog log)
        {
            _tableStorage = AzureTableStorage<ProcessedBlockEntity>.Create(connectionStringManager, "ProcessedBlocks", log);
        }

        public async Task<int> GetLastProcessedBlockAsync()
        {
            var entity = await _tableStorage.GetDataAsync(GetPartitionKey(), GetRowKey());

                if (entity != null)
                return entity.Height;
            else
                return 0;
        }

        public async Task SetLastProcessedBlockAsync(int height)
        {
            await _tableStorage.InsertOrReplaceAsync(new ProcessedBlockEntity
            {
                Height = height,
                PartitionKey = GetPartitionKey(),
                RowKey = GetRowKey()
            });
        }
    }
}
