using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using AzureStorage;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Addresses;

namespace Lykke.Job.IcoBtcTransactionTracker.AzureRepositories
{
    public class AddressRepository : IAddressRepository
    {
        private INoSQLTableStorage<Address> _tableStorage;

        public AddressRepository(INoSQLTableStorage<Address> tableStorage)
        {
            _tableStorage = tableStorage;
        }

        public async Task<string> GetUserEmailByAddressAsync(string currency, string address)
        {
            return (await _tableStorage.GetDataAsync(currency, address))?.Email;
        }
    }
}
