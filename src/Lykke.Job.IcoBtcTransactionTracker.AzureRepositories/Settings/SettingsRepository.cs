using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using AzureStorage;
using AzureStorage.Tables;
using Common.Log;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Settings;
using Lykke.SettingsReader;

namespace Lykke.Job.IcoBtcTransactionTracker.AzureRepositories.Settings
{
    public class SettingsRepository : ISettingsRepository
    {
        private readonly INoSQLTableStorage<SettingsEntity> _tableStorage;
        private static string GetPartitionKey() => "Settings";
        private static string GetRowKey() => string.Empty;

        public SettingsRepository(IReloadingManager<string> connectionStringManager, ILog log)
        {
            _tableStorage = AzureTableStorage<SettingsEntity>.Create(connectionStringManager, "IcoBtcTransactionTrackerSettings", log);
        }

        public async Task<ulong> GetLastProcessedBlockHeightAsync()
        {
            var partitionKey = GetPartitionKey();
            var rowKey = GetRowKey();

            return (await _tableStorage.GetDataAsync(partitionKey, rowKey))?.LastProcessedBlockHeight ?? 0;
        }

        public async Task UpdateLastProcessedBlockHeightAsync(ulong height)
        {
            var partitionKey = GetPartitionKey();
            var rowKey = GetRowKey();

            await _tableStorage.InsertOrReplaceAsync(new SettingsEntity
            {
                PartitionKey = partitionKey,
                RowKey = rowKey,
                LastProcessedBlockHeight = height
            });
        }
    }
}
