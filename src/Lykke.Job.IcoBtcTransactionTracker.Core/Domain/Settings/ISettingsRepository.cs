using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Settings
{
    public interface ISettingsRepository
    {
        Task<ulong> GetLastProcessedBlockHeightAsync();
        Task UpdateLastProcessedBlockHeightAsync(ulong height);
    }
}
