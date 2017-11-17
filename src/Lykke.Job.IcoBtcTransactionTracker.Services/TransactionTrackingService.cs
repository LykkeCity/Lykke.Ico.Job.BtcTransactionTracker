using System;
using System.Net.Http;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Addresses;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.ProcessedBlocks;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;
using Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings;
using Newtonsoft.Json;

namespace Lykke.Job.IcoBtcTransactionTracker.Services
{
    public class TransactionTrackingService : ITransactionTrackingService
    {
        private ILog _log;
        private TrackingSettings _trackingSettings;
        private IProcessedBlockRepository _processedBlockRepository;
        private IAddressRepository _addressRepository;
        private HttpClient _ninjaHttpClient = new HttpClient();
        private NBitcoin.Network _ninjaNetwork;

        public TransactionTrackingService(ILog log, TrackingSettings trackingSettings, 
            IProcessedBlockRepository processedBlockRepository,
            IAddressRepository addressRepository)
        {
            _log = log;
            _trackingSettings = trackingSettings;
            _processedBlockRepository = processedBlockRepository;
            _addressRepository = addressRepository;
            _ninjaNetwork = NBitcoin.Network.GetNetwork(trackingSettings.NinjaNetwork) ?? NBitcoin.Network.RegTest;

            _ninjaHttpClient.BaseAddress = new Uri(trackingSettings.NinjaUrl);  
        }

        public async Task Execute()
        {
            var lastProcessedHeight = _trackingSettings.StartHeight;
            var lastProcessed = await _processedBlockRepository.GetLastProcessedBlockAsync();
            var lastConfirmed = await GetLastConfirmedBlockAsync();

            if (lastProcessed != null)
            {
                lastProcessedHeight = lastProcessed.BlockHeight;
            }

            if (lastConfirmed.AdditionalInformation.Height == lastProcessedHeight && lastConfirmed.AdditionalInformation.BlockId != lastProcessed.BlockId)
            {
                // fork detected!
                var msg = string.Format("Fork on height {0}: {1} <> {2}", 
                    lastProcessedHeight, 
                    lastConfirmed.AdditionalInformation.BlockId, 
                    lastProcessed.BlockId);

                await _log.WriteWarningAsync(nameof(IcoBtcTransactionTracker), nameof(TransactionTrackingService), nameof(Execute), msg);

                // process last confirmed because it hasn't been processed yet
                await ProcessBlock(lastProcessedHeight);
            }
            else
            {
                // process all blocks from last processed to the end
                for (int h = lastProcessedHeight + 1; h <= lastConfirmed.AdditionalInformation.Height; h++)
                {
                    await ProcessBlock(h);
                }
            }
        }

        private async Task ProcessBlock(int height)
        {
            var block = await GetConfirmedBlockByHeightAsync(height);

            var blockContent = NBitcoin.Block.Parse(block.AdditionalInformation.Block);

            foreach (var tx in blockContent.Transactions)
            {
                foreach(var output in tx.Outputs)
                {
                    var payINAddress = output.ScriptPubKey.GetDestinationAddress(_ninjaNetwork);
                    var email = await _addressRepository.GetUserEmailByAddressAsync("btc", payINAddress.ToString());

                    if (email != null)
                    {

                    }
                }
            }
        }

        private async Task<Block> GetLastConfirmedBlockAsync()
        {
            var backTo = _trackingSettings.ConfirmationLimit > 0 ? _trackingSettings.ConfirmationLimit - 1 : 0;
            var url = string.Format("blocks/tip-{0}?format=json&headeronly=true", backTo);
            var blockJson = await _ninjaHttpClient.GetStringAsync(url);

            return JsonConvert.DeserializeObject<Block>(blockJson);
        }

        private async Task<Block> GetConfirmedBlockByHeightAsync(int height)
        {
            var url = "blocks/" + height;
            var blockJson = await DoNinjaRequest(url);

            return JsonConvert.DeserializeObject<Block>(blockJson);
        }

        private async Task<string> DoNinjaRequest(string url)
        {
            var result = await _ninjaHttpClient.GetAsync(url);

            try
            {
                result.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                await _log.WriteErrorAsync(nameof(IcoBtcTransactionTracker), nameof(TransactionTrackingService), nameof(DoNinjaRequest), ex);
                throw;
            }

            return await result.Content.ReadAsStringAsync();
        }

        private class Block
        {
            public BlockAdditionalInformation AdditionalInformation { get; set; }
        }

        private class BlockAdditionalInformation
        {
            public string BlockId { get; set; }
            public string Block { get; set; }
            public int Height { get; set; }
            public int Confirmations { get; set; }
        }
    }
}
