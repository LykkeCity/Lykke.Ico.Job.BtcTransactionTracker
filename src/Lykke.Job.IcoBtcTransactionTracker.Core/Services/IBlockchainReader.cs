using System;
using System.Threading.Tasks;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Services
{
    public interface IBlockchainReader
    {
        Task<BlockInformation> GetBlockByHeightAsync(UInt64 height);
        Task<BlockInformation> GetLastConfirmedBlockAsync(UInt64 confirmationLimit = 0);
    }
}
