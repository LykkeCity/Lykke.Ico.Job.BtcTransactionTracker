using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Ico.Core;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Ico.Core.Repositories.CampaignInfo;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;
using Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings;
using NBitcoin;
using Newtonsoft.Json;

namespace Lykke.Job.IcoBtcTransactionTracker.Services
{
    public class TransactionTrackingService : ITransactionTrackingService
    {
        private readonly ILog _log;
        private readonly TrackingSettings _trackingSettings;
        private readonly ICampaignInfoRepository _campaignInfoRepository;
        private readonly IQueuePublisher<BlockchainTransactionMessage> _transactionQueue;
        private readonly HttpClient _ninjaHttpClient = new HttpClient();
        private readonly Network _ninjaNetwork;
        private readonly string _component = nameof(TransactionTrackingService);
        private readonly string _process = nameof(Execute);

        public TransactionTrackingService(
            ILog log,
            TrackingSettings trackingSettings, 
            ICampaignInfoRepository campaignInfoRepository,
            IQueuePublisher<BlockchainTransactionMessage> transactionQueue)
        {
            _log = log;
            _trackingSettings = trackingSettings;
            _campaignInfoRepository = campaignInfoRepository;
            _transactionQueue = transactionQueue;
            _ninjaNetwork = Network.GetNetwork(trackingSettings.NinjaNetwork) ?? Network.RegTest;
            _ninjaHttpClient.BaseAddress = new Uri(trackingSettings.NinjaUrl);  
        }

        public async Task Execute()
        {
            ulong lastProcessedHeight = 0;
            var lastConfirmed = await GetLastConfirmedBlockAsync();

            if (!ulong.TryParse(await _campaignInfoRepository.GetValueAsync(CampaignInfoType.LastProcessedBlockBtc), out lastProcessedHeight) ||
                lastProcessedHeight == 0)
            {
                lastProcessedHeight = _trackingSettings.StartHeight;
            }

            if (lastProcessedHeight >= lastConfirmed.AdditionalInformation.Height)
            {
                // all processed or start height is greater than current height
                return;
            }

            var from = lastProcessedHeight + 1;
            var to = lastConfirmed.AdditionalInformation.Height;
            var blockCount = to - lastProcessedHeight;
            var blockRange = blockCount > 1 ? $"[{from} - {to}]" : $"[{to}]";
            var txCount = 0;

            await _log.WriteInfoAsync(_component, _process, _ninjaNetwork.Name, $"Processing block(s) {blockRange} started");

            for (var h = from; h <= to; h++)
            {
                txCount += await ProcessBlock(h);
            }

            await _log.WriteInfoAsync(_component, _process, _ninjaNetwork.Name, $"{blockCount} block(s) processed; {txCount} payment transactions queued");
        }

        private async Task<int> ProcessBlock(ulong height)
        {
            var blockInfo = await GetConfirmedBlockByHeightAsync(height);
            var block = Block.Parse(blockInfo.Block);
            var blockId = blockInfo.AdditionalInformation.BlockId;
            var blockTimestamp = blockInfo.AdditionalInformation.BlockTime;
            var txCount = 0;

            foreach (var tx in block.Transactions)
            {
                var coins = tx.Outputs.AsCoins()
                    .Where(c => c.ScriptPubKey.IsValid && c.Amount.Satoshi > 0);

                foreach (var coin in coins)
                {
                    var destAddress = coin.ScriptPubKey.GetDestinationAddress(_ninjaNetwork);
                    if (destAddress == null)
                    {
                        // not a payment
                        continue;
                    }

                    var bitcoinAmount = coin.Amount.ToUnit(MoneyUnit.BTC);
                    var transactionId = coin.Outpoint.ToString();

                    await _transactionQueue.SendAsync(new BlockchainTransactionMessage
                    {
                        BlockId = blockId,
                        BlockTimestamp = blockTimestamp,
                        TransactionId = transactionId,
                        DestinationAddress = destAddress.ToString(),
                        CurrencyType = CurrencyType.Bitcoin,
                        Amount = bitcoinAmount,
                    });

                    txCount++;
                }
            }

            await _campaignInfoRepository.SaveValueAsync(CampaignInfoType.LastProcessedBlockBtc, height.ToString());

            return txCount;
        }

        private async Task<BlockInformation> GetLastConfirmedBlockAsync()
        {
            var backTo = _trackingSettings.ConfirmationLimit > 0 ? _trackingSettings.ConfirmationLimit - 1 : 0;
            var url = $"blocks/tip-{backTo}?format=json&headeronly=true";

            return await DoNinjaRequest<BlockInformation>(url);
        }

        private async Task<BlockInformation> GetConfirmedBlockByHeightAsync(UInt64 height)
        {
            return await DoNinjaRequest<BlockInformation>($"blocks/{height}");
        }

        private async Task<T> DoNinjaRequest<T>(string url)
        {
            var resp = await _ninjaHttpClient.GetAsync(url);

            resp.EnsureSuccessStatusCode();

            return JsonConvert.DeserializeObject<T>(await resp.Content.ReadAsStringAsync());
        }

        private class BlockInformation
        {
            public BlockAdditionalInformation AdditionalInformation { get; set; }
            public String Block { get; set; }
        }

        private class BlockAdditionalInformation
        {
            public String BlockId { get; set; }
            public DateTimeOffset BlockTime { get; set; }
            public UInt64 Height { get; set; }
            public UInt64 Confirmations { get; set; }
        }
    }
}
