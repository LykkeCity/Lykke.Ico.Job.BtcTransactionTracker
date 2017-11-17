using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Addresses
{
    public interface IAddressRepository
    {
        Task<string> GetUserEmailByAddressAsync(string currency, string address);
    }
}
