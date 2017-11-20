using System;
using System.Net.Http;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Ico.Core.Contracts.Repositories;
using Lykke.Ico.Core.Repositories.InvestorAttribute;
using Lykke.Job.IcoBtcTransactionTracker.Core;
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
        private IInvestorAttributeRepository _investorAttributeRepository;
        private HttpClient _ninjaHttpClient = new HttpClient();
        private NBitcoin.Network _ninjaNetwork;

        public TransactionTrackingService(ILog log, TrackingSettings trackingSettings, 
            IProcessedBlockRepository processedBlockRepository,
            IInvestorAttributeRepository investorAttributeRepository)
        {
            _log = log;
            _trackingSettings = trackingSettings;
            _processedBlockRepository = processedBlockRepository;
            _investorAttributeRepository = investorAttributeRepository;
            _ninjaNetwork = NBitcoin.Network.GetNetwork(trackingSettings.NinjaNetwork) ?? NBitcoin.Network.RegTest;

            _ninjaHttpClient.BaseAddress = new Uri(trackingSettings.NinjaUrl);  
        }

        public async Task Execute()
        {
            var lastProcessedHeight = await _processedBlockRepository.GetLastProcessedBlockAsync();
            var lastConfirmed = await GetLastConfirmedBlockAsync();

            if (lastProcessedHeight == 0)
            {
                lastProcessedHeight = _trackingSettings.StartHeight;
            }

            for (int h = lastProcessedHeight + 1; h <= lastConfirmed.AdditionalInformation.Height; h++)
            {
                await ProcessBlock(h);
            }
        }

        private async Task ProcessBlock(int height)
        {
            var blockInfo = await GetConfirmedBlockByHeightAsync(height);
            var block = NBitcoin.Block.Parse(blockInfo.Block);

            foreach (var tx in block.Transactions)
            {
                foreach(var output in tx.Outputs)
                {
                    if (!output.ScriptPubKey.IsValid)
                    {
                        // should we do smth?
                        continue;
                    }

                    var cashInAddress = output.ScriptPubKey.GetDestinationAddress(_ninjaNetwork);
                    if (cashInAddress == null)
                    {
                        // not a payment
                        continue;
                    }

                    // check if destination address is cash-in address of ICO investor
                    var userMail = await _investorAttributeRepository.GetInvestorEmailAsync(InvestorAttributeType.BthPublicKey, cashInAddress.ToString());
                    if (userMail != null)
                    {
                        // TODO: 
                        // - calc USD amount, if > 5k redirect user to KYC
                        // - increase the total ICO amount
                        // - save investement info for investor
                        // - send confirmation email
                    }
                }
            }

            await _processedBlockRepository.SetLastProcessedBlockAsync(height);
        }

        private async Task<BlockInformation> GetLastConfirmedBlockAsync()
        {
            var backTo = _trackingSettings.ConfirmationLimit > 0 ? _trackingSettings.ConfirmationLimit - 1 : 0;
            var url = string.Format("blocks/tip-{0}?format=json&headeronly=true", backTo);
            var blockJson = await _ninjaHttpClient.GetStringAsync(url);

            return JsonConvert.DeserializeObject<BlockInformation>(blockJson);
        }

        private async Task<BlockInformation> GetConfirmedBlockByHeightAsync(int height)
        {
            var url = "blocks/" + height;
            var blockJson = await DoNinjaRequest(url);

            return JsonConvert.DeserializeObject<BlockInformation>(blockJson);
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

        private class BlockInformation
        {
            public BlockAdditionalInformation AdditionalInformation { get; set; }
            public string Block { get; set; }
        }

        private class BlockAdditionalInformation
        {
            public string BlockId { get; set; }
            public DateTimeOffset BlockTime { get; set; }
            public int Height { get; set; }
            public int Confirmations { get; set; }
        }
    }
}
