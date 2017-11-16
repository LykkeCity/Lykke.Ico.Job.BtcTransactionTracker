using System.Threading.Tasks;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Services
{
    public interface IShutdownManager
    {
        Task StopAsync();
    }
}