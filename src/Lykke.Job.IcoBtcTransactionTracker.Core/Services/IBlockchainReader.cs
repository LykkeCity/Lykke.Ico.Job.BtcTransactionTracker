using System;
using System.Threading.Tasks;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Services
{
    public interface IBlockchainReader
    {
        Task<BlockInformation> GetBlockByHeightAsync(ulong height);
        Task<BlockInformation> GetBlockByIdAsync(string id);
        Task<BlockInformation> GetLastConfirmedBlockAsync(ulong confirmationLimit = 0);
    }
}
