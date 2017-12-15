using System;
using System.Linq;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Ico.Core;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Ico.Core.Repositories.CampaignInfo;
using Lykke.Ico.Core.Repositories.InvestorAttribute;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;
using Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings;
using NBitcoin;

namespace Lykke.Job.IcoBtcTransactionTracker.Services
{
    public class TransactionTrackingService : ITransactionTrackingService
    {
        private readonly ILog _log;
        private readonly TrackingSettings _trackingSettings;
        private readonly ICampaignInfoRepository _campaignInfoRepository;
        private readonly IInvestorAttributeRepository _investorAttributeRepository;
        private readonly IQueuePublisher<TransactionMessage> _transactionQueue;
        private readonly IBlockchainReader _blockchainReader;
        private readonly Network _network;

        public TransactionTrackingService(
            ILog log,
            TrackingSettings trackingSettings, 
            ICampaignInfoRepository campaignInfoRepository,
            IInvestorAttributeRepository investorAttributeRepository,
            IQueuePublisher<TransactionMessage> transactionQueue,
            IBlockchainReader blockchainReader)
        {
            _log = log;
            _trackingSettings = trackingSettings;
            _campaignInfoRepository = campaignInfoRepository;
            _investorAttributeRepository = investorAttributeRepository;
            _transactionQueue = transactionQueue;
            _blockchainReader = blockchainReader;
            _network = Network.GetNetwork(trackingSettings.BtcNetwork);
        }

        public async Task Track()
        {
            var lastConfirmed = await _blockchainReader.GetLastConfirmedBlockAsync(_trackingSettings.ConfirmationLimit);
            if (lastConfirmed == null)
            {
                throw new InvalidOperationException("Cannot get last confirmed block");
            }

            var lastProcessedBlockBtc = await _campaignInfoRepository.GetValueAsync(CampaignInfoType.LastProcessedBlockBtc);
            if (!ulong.TryParse(lastProcessedBlockBtc, out var lastProcessedHeight) || lastProcessedHeight == 0)
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
            var blockRange = blockCount > 1 ? $"[{from} - {to}, {blockCount}]" : $"[{to}]";
            var txCount = 0;

            await _log.WriteInfoAsync(nameof(Track),
                $"Network: {_network.Name}, Range: {blockRange}", 
                $"Processing started");

            for (var h = from; h <= to; h++)
            {
                txCount += await ProcessBlock(h);
                await _campaignInfoRepository.SaveValueAsync(CampaignInfoType.LastProcessedBlockBtc, h.ToString());
            }

            await _log.WriteInfoAsync(nameof(Track),
                $"Network: {_network.Name}, Range: {blockRange}, Investments: {txCount}", 
                $"Processing completed");
        }

        private async Task<int> ProcessBlock(ulong height)
        {
            var blockInfo = await _blockchainReader.GetBlockByHeightAsync(height);
            if (blockInfo == null)
            {
                await _log.WriteWarningAsync(nameof(ProcessBlock),
                    $"Network: {_network.Name}, Block: {height}", 
                    $"Block {height} not found or invalid, therefore skipped");
                return 0;
            }

            var block = Block.Parse(blockInfo.Block);
            var blockId = blockInfo.AdditionalInformation.BlockId;
            var blockTime = blockInfo.AdditionalInformation.BlockTime;
            var txCount = 0;

            foreach (var tx in block.Transactions)
            {
                var coins = tx.Outputs.AsCoins().Where(c => c.ScriptPubKey.IsValid && c.Amount.Satoshi > 0);
                foreach (var coin in coins)
                {
                    var destAddress = coin.ScriptPubKey.GetDestinationAddress(_network);
                    if (destAddress == null)
                    {
                        // not a payment
                        continue;
                    }

                    var investorEmail = await _investorAttributeRepository.GetInvestorEmailAsync(InvestorAttributeType.PayInBtcAddress, destAddress.ToString());
                    if (string.IsNullOrWhiteSpace(investorEmail))
                    {
                        // destination address is not a cash-in address of any ICO investor
                        continue;
                    }

                    var transactionId = coin.Outpoint.Hash.ToString();

                    await _transactionQueue.SendAsync(new TransactionMessage
                    {
                        Email = investorEmail,
                        UniqueId = coin.Outpoint.ToString(),
                        Currency = CurrencyType.Bitcoin,
                        TransactionId = transactionId,
                        BlockId = blockId,
                        CreatedUtc = blockTime.UtcDateTime,
                        PayInAddress = destAddress.ToString(),
                        Amount = coin.Amount.ToUnit(MoneyUnit.BTC),
                        Link = $"{_trackingSettings.BtcTrackerUrl}tx/{transactionId}"
                    });

                    txCount++;
                }
            }

            await _log.WriteInfoAsync(nameof(ProcessBlock),
                $"Network: {_network.Name}, Block: {height}, Investments: {txCount}",
                $"Block {height} processed");

            return txCount;
        }
    }
}
