using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;
using Newtonsoft.Json;

namespace Lykke.Job.IcoBtcTransactionTracker.Services
{
    public class BlockchainReader : IBlockchainReader
    {
        private readonly HttpClient _ninjaHttpClient = new HttpClient();

        public BlockchainReader(string ninjaUrl)
        {
            _ninjaHttpClient.BaseAddress = new Uri(ninjaUrl);
        }

        public async Task<BlockInformation> GetLastConfirmedBlockAsync(UInt64 confirmationLimit = 0)
        {
            var url = confirmationLimit > 0 ?
                $"blocks/tip-{confirmationLimit - 1}?headeronly=true" :
                $"blocks/tip?headeronly=true";

            return await DoNinjaRequest<BlockInformation>(url);
        }

        public async Task<BlockInformation> GetBlockByHeightAsync(UInt64 height)
        {
            return await DoNinjaRequest<BlockInformation>($"blocks/{height}");
        }

        private async Task<T> DoNinjaRequest<T>(String url)
        {
            var resp = await _ninjaHttpClient.GetAsync(url);

            resp.EnsureSuccessStatusCode();

            return JsonConvert.DeserializeObject<T>(await resp.Content.ReadAsStringAsync());
        }
    }
}
