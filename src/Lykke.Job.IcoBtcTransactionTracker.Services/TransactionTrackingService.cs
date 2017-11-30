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
        private readonly IBlockchainReader _blockchainReader;
        private readonly Network _ninjaNetwork;
        private readonly string _component = nameof(TransactionTrackingService);
        private readonly string _process = nameof(Execute);
        private readonly string _link;

        public TransactionTrackingService(
            ILog log,
            TrackingSettings trackingSettings, 
            ICampaignInfoRepository campaignInfoRepository,
            IQueuePublisher<BlockchainTransactionMessage> transactionQueue,
            IBlockchainReader blockchainReader)
        {
            _log = log;
            _trackingSettings = trackingSettings;
            _campaignInfoRepository = campaignInfoRepository;
            _transactionQueue = transactionQueue;
            _blockchainReader = blockchainReader;
            _ninjaNetwork = Network.GetNetwork(trackingSettings.NinjaNetwork) ?? Network.TestNet;
            _link = _ninjaNetwork == Network.Main ?
                "https://blockchainexplorer.lykke.com/transaction" :
                "https://live.blockcypher.com/btc-testnet/tx";
        }

        public async Task Execute()
        {
            ulong lastProcessedHeight = 0;

            var lastConfirmed = await _blockchainReader.GetLastConfirmedBlockAsync(_trackingSettings.ConfirmationLimit);
            if (lastConfirmed == null)
            {
                throw new InvalidOperationException("Cannot get last confirmed block");
            }

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

            await _log.WriteInfoAsync(_component, _process, _ninjaNetwork.Name, 
                $"Processing block(s) {blockRange} started");

            for (var h = from; h <= to; h++)
            {
                txCount += await ProcessBlock(h);
                await _campaignInfoRepository.SaveValueAsync(CampaignInfoType.LastProcessedBlockBtc, h.ToString());
            }

            await _log.WriteInfoAsync(_component, _process, _ninjaNetwork.Name, 
                $"Processing block(s) {blockRange} completed; {blockCount} block(s) processed; {txCount} payment transactions queued");
        }

        private async Task<int> ProcessBlock(ulong height)
        {
            var blockInfo = await _blockchainReader.GetBlockByHeightAsync(height);
            if (blockInfo == null)
            {
                await _log.WriteWarningAsync(_component, _process, _ninjaNetwork.Name, 
                    $"Block [{height}] not found or invalid; block skipped");
                return 0;
            }

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
                    var link = $"{_link}/{coin.Outpoint.Hash}";

                    await _transactionQueue.SendAsync(new BlockchainTransactionMessage
                    {
                        BlockId = blockId,
                        BlockTimestamp = blockTimestamp,
                        TransactionId = transactionId,
                        DestinationAddress = destAddress.ToString(),
                        CurrencyType = CurrencyType.Bitcoin,
                        Amount = bitcoinAmount,
                        Link = link
                    });

                    txCount++;
                }
            }

            await _log.WriteInfoAsync(_component, _process, _ninjaNetwork.Name, 
                $"Block [{height}] processed; {txCount} payment transactions queued");

            return txCount;
        }
    }
}
