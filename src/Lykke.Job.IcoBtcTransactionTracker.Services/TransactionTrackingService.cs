using System.Threading.Tasks;
using Common.Log;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;

namespace Lykke.Job.IcoBtcTransactionTracker.Services
{
    public class TransactionTrackingService : ITransactionTrackingService
    {
        private ILog _log;

        public TransactionTrackingService(ILog log)
        {
        }

        public async Task Execute()
        {
            await Task.CompletedTask;
        }
    }
}