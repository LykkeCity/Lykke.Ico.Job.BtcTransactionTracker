using System;
using System.Collections.Generic;
using System.Text;
using Lykke.AzureStorage.Tables;
using Lykke.Job.IcoBtcTransactionTracker.Core;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.ProcessedBlocks;
using Microsoft.WindowsAzure.Storage.Table;

namespace Lykke.Job.IcoBtcTransactionTracker.AzureRepositories
{
    public class ProcessedBlockEntity : TableEntity
    {
        public int Height { get; set; }
    }
}
