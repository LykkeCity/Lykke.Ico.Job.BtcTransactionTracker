using System;
using System.Collections.Generic;
using System.Text;
using Lykke.AzureStorage.Tables;
using Lykke.AzureStorage.Tables.Entity.Annotation;
using Lykke.AzureStorage.Tables.Entity.ValueTypesMerging;

namespace Lykke.Job.IcoBtcTransactionTracker.AzureRepositories.Settings
{
    [ValueTypeMergingStrategyAttribute(ValueTypeMergingStrategy.UpdateAlways)]
    public class SettingsEntity : AzureTableEntity
    {
        public ulong LastProcessedBlockHeight { get; set; }
    }
}
