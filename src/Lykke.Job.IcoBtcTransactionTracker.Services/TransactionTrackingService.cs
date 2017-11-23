using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Lykke.Ico.Core;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.ProcessedBlocks;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;
using Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings;
using NBitcoin;
using Newtonsoft.Json;

namespace Lykke.Job.IcoBtcTransactionTracker.Services
{
    public class TransactionTrackingService : ITransactionTrackingService
    {
        private readonly TrackingSettings _trackingSettings;
        private readonly IProcessedBlockRepository _processedBlockRepository;
        private readonly IQueuePublisher<BlockchainTransactionMessage> _transactionQueue;
        private readonly HttpClient _ninjaHttpClient = new HttpClient();
        private readonly Network _ninjaNetwork;

        public TransactionTrackingService(
            TrackingSettings trackingSettings, 
            IProcessedBlockRepository processedBlockRepository,
            IQueuePublisher<BlockchainTransactionMessage> transactionQueue)
        {
            _trackingSettings = trackingSettings;
            _processedBlockRepository = processedBlockRepository;
            _transactionQueue = transactionQueue;
            _ninjaNetwork = Network.GetNetwork(trackingSettings.NinjaNetwork) ?? Network.RegTest;
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
            var block = Block.Parse(blockInfo.Block);
            var blockId = blockInfo.AdditionalInformation.BlockId;
            var blockTimestamp = blockInfo.AdditionalInformation.BlockTime;

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
                        Amount = bitcoinAmount,
                        BlockTimestamp = blockTimestamp,
                        CurrencyType = CurrencyType.Bitcoin,
                        DestinationAddress = destAddress.ToString(),
                        TransactionId = transactionId,
                    });
                }
            }

            await _processedBlockRepository.SetLastProcessedBlockAsync(height);
        }

        private async Task<BlockInformation> GetLastConfirmedBlockAsync()
        {
            var backTo = _trackingSettings.ConfirmationLimit > 0 ? _trackingSettings.ConfirmationLimit - 1 : 0;
            var url = $"blocks/tip-{backTo}?format=json&headeronly=true";

            return await DoNinjaRequest<BlockInformation>(url);
        }

        private async Task<BlockInformation> GetConfirmedBlockByHeightAsync(int height)
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
