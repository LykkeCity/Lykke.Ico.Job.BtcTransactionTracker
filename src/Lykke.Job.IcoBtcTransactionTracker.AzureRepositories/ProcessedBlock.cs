using System;
using System.Collections.Generic;
using System.Text;
using Lykke.AzureStorage.Tables;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.ProcessedBlocks;
using Microsoft.WindowsAzure.Storage.Table;

namespace Lykke.Job.IcoBtcTransactionTracker.AzureRepositories
{
    public class ProcessedBlock : TableEntity, IProcessedBlock
    {
        public const string PK_LAST = "Last";
        public const string PK_PREV = "Prev";
        public const string PK_FORK = "Fork";

        public ProcessedBlock()
        {
            PartitionKey = PK_LAST;
        }

        public ProcessedBlock(long height) : this()
        {
            RowKey = height.ToString();
        }

        public string BlockId { get; set; }

        public int BlockHeight { get; set; }
    }
}
