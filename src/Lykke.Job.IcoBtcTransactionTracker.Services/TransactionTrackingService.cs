using System;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Ico.Core;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Ico.Core.Repositories.CampaignInfo;
using Lykke.Ico.Core.Repositories.InvestorAttribute;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain;
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
            if (!ulong.TryParse(lastProcessedBlockBtc, out var lastProcessedHeight) || lastProcessedHeight < _trackingSettings.StartHeight)
            {
                lastProcessedHeight = _trackingSettings.StartHeight;
            }

            if (lastProcessedHeight >= lastConfirmed.AdditionalInformation.Height)
            {
                // all processed or start height is greater than current height
                await _log.WriteInfoAsync(nameof(Track),
                    $"Network: {_network}, LastProcessedHeight: {lastProcessedHeight}, LastConfirmedHeight: {lastConfirmed.AdditionalInformation.Height}",
                    $"No new data");

                return;
            }

            await ProcessRange(
                lastProcessedHeight + 1, 
                lastConfirmed.AdditionalInformation.Height,
                saveProgress: true);
        }

        public async Task<int> ProcessBlock(BlockInformation blockInfo)
        {
            if (blockInfo == null)
            {
                throw new ArgumentNullException(nameof(blockInfo));
            }

            if (blockInfo.AdditionalInformation.Confirmations < _trackingSettings.ConfirmationLimit)
            {
                await _log.WriteWarningAsync(nameof(ProcessBlock),
                    $"Network: {_network.Name}, Block: {blockInfo.AdditionalInformation.ToJson()}",
                    $"Insufficient confirmation count for block {blockInfo.AdditionalInformation.Height}, therefore skipped");

                return 0;
            }

            var block = Block.Parse(blockInfo.Block);
            var count = 0;

            foreach (var tx in block.Transactions)
            {
                var coins = tx.Outputs.AsCoins()
                    .Where(c => c.ScriptPubKey.IsValid && c.Amount.Satoshi > 0)
                    .ToArray();

                foreach (var coin in coins)
                {
                    var payInAddress = coin.ScriptPubKey.GetDestinationAddress(_network);
                    if (payInAddress == null)
                    {
                        // not a payment
                        continue;
                    }

                    var email = await _investorAttributeRepository.GetInvestorEmailAsync(InvestorAttributeType.PayInBtcAddress, payInAddress.ToString());
                    if (string.IsNullOrWhiteSpace(email))
                    {
                        // destination address is not a cash-in address of any ICO investor
                        continue;
                    }

                    var transactionId = coin.Outpoint.Hash.ToString();

                    await _transactionQueue.SendAsync(new TransactionMessage
                    {
                        Email = email,
                        UniqueId = coin.Outpoint.ToString(),
                        Currency = CurrencyType.Bitcoin,
                        TransactionId = transactionId,
                        BlockId = blockInfo.AdditionalInformation.BlockId,
                        CreatedUtc = blockInfo.AdditionalInformation.BlockTime.UtcDateTime,
                        PayInAddress = payInAddress.ToString(),
                        Amount = coin.Amount.ToUnit(MoneyUnit.BTC),
                        Link = $"{_trackingSettings.BtcTrackerUrl}tx/{transactionId}"
                    });

                    count++;
                }
            }

            await _log.WriteInfoAsync(nameof(ProcessBlock),
                $"Network: {_network.Name}, Block: {blockInfo.AdditionalInformation.ToJson()}, Investments: {count}",
                $"Block {blockInfo.AdditionalInformation.Height} processed");

            return count;
        }

        public async Task<int> ProcessBlockByHeight(ulong height)
        {
            var blockInfo = await _blockchainReader.GetBlockByHeightAsync(height);
            if (blockInfo == null)
            {
                await _log.WriteWarningAsync(nameof(ProcessBlockByHeight),
                    $"Network: {_network.Name}, Block: {height}",
                    $"Block {height} not found or invalid, therefore skipped");

                return 0;
            }

            return await ProcessBlock(blockInfo);
        }

        public async Task<int> ProcessBlockById(string id)
        {
            var blockInfo = await _blockchainReader.GetBlockByIdAsync(id);
            if (blockInfo == null)
            {
                await _log.WriteWarningAsync(nameof(ProcessBlockById),
                    $"Network: {_network.Name}, Block: {id}",
                    $"Block {id} not found or invalid, therefore skipped");

                return 0;
            }

            return await ProcessBlock(blockInfo);
        }

        public async Task<int> ProcessRange(ulong fromHeight, ulong toHeight, bool saveProgress = true)
        {
            if (fromHeight > toHeight)
            {
                throw new ArgumentException("Invalid range");
            }

            var blockRange = toHeight > fromHeight ? 
                $"[{fromHeight} - {toHeight}, {toHeight - fromHeight + 1}]" :
                $"[{fromHeight}]";

            var txCount = 0;

            await _log.WriteInfoAsync(nameof(ProcessRange),
                $"Network: {_network.Name}, Range: {blockRange}",
                $"Range processing started");

            for (var h = fromHeight; h <= toHeight; h++)
            {
                txCount += await ProcessBlockByHeight(h);

                if (saveProgress)
                {
                    await _campaignInfoRepository.SaveValueAsync(CampaignInfoType.LastProcessedBlockBtc, h.ToString());
                }
            }

            await _log.WriteInfoAsync(nameof(ProcessRange),
                $"Network: {_network.Name}, Range: {blockRange}, Investments: {txCount}",
                $"Range processing completed");

            return txCount;
        }
    }
}
