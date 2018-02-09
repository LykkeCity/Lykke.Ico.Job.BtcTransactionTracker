using System;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Settings;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;
using Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings;
using Lykke.Service.IcoCommon.Client;
using Lykke.Service.IcoCommon.Client.Models;
using NBitcoin;

namespace Lykke.Job.IcoBtcTransactionTracker.Services
{
    public class TransactionTrackingService : ITransactionTrackingService
    {
        private readonly ILog _log;
        private readonly TrackingSettings _trackingSettings;
        private readonly ISettingsRepository _settingsRepository;
        private readonly IBlockchainReader _blockchainReader;
        private readonly IIcoCommonServiceClient _commonServiceClient;
        private readonly Network _network;

        public TransactionTrackingService(
            ILog log,
            TrackingSettings trackingSettings, 
            ISettingsRepository settingsRepository,
            IBlockchainReader blockchainReader,
            IIcoCommonServiceClient commonServiceClient)
        {
            _log = log;
            _trackingSettings = trackingSettings;
            _settingsRepository = settingsRepository;
            _blockchainReader = blockchainReader;
            _commonServiceClient = commonServiceClient;
            _network = Network.GetNetwork(trackingSettings.BtcNetwork);
        }

        public async Task Track()
        {
            var lastConfirmed = await _blockchainReader.GetLastConfirmedBlockAsync(_trackingSettings.ConfirmationLimit);
            if (lastConfirmed == null)
            {
                throw new InvalidOperationException("Cannot get last confirmed block");
            }

            var lastProcessedHeight = await _settingsRepository.GetLastProcessedBlockHeightAsync();
            if (lastProcessedHeight < _trackingSettings.StartHeight)
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

            var blockTransactions = block.Transactions
                .SelectMany(tx => tx.Outputs.AsCoins()
                    .Select(coin => new { coin, address = coin.ScriptPubKey.GetDestinationAddress(_network) })
                    .Where(x => x.coin.ScriptPubKey.IsValid && x.coin.Amount.Satoshi > 0)
                    .Where(x => x.address != null))
                .Select(x => new TransactionModel()
                {
                    Amount = x.coin.Amount.ToDecimal(MoneyUnit.BTC),
                    BlockId = blockInfo.AdditionalInformation.BlockId,
                    CreatedUtc = blockInfo.AdditionalInformation.BlockTime.UtcDateTime,
                    Currency = CurrencyType.BTC,
                    PayInAddress = x.address.ToString(),
                    TransactionId = x.coin.Outpoint.Hash.ToString(),
                    UniqueId = x.coin.Outpoint.ToString()
                })
                .ToList();

            var count = 0;

            if (blockTransactions.Any())
            {
                count = await _commonServiceClient.HandleTransactionsAsync(blockTransactions);
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
                    await _settingsRepository.UpdateLastProcessedBlockHeightAsync(h);
                }
            }

            await _log.WriteInfoAsync(nameof(ProcessRange),
                $"Network: {_network.Name}, Range: {blockRange}, Investments: {txCount}",
                $"Range processing completed");

            return txCount;
        }
    }
}
