using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Settings;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;
using Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings;
using Lykke.Job.IcoBtcTransactionTracker.Services;
using Lykke.Service.IcoCommon.Client;
using Lykke.Service.IcoCommon.Client.Models;
using Moq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Xunit;

namespace Lykke.Job.IcoBtcTransactionTracker.Tests
{
    public class TransactionTrackingServiceTests
    {
        private ILog _log;
        private TrackingSettings _trackingSettings;
        private Mock<ISettingsRepository> _settingsRepository;
        private Mock<IIcoCommonServiceClient> _commonServiceClient;
        private Mock<IBlockchainReader> _blockchainReader;
        private Network _network = Network.TestNet;
        private ulong _lastProcessed;

        public ulong LastProcessed
        {
            get => _lastProcessed;
            set => _lastProcessed = value;
        }

        public ITransactionTrackingService Init(
            ulong lastProcessed = 0,
            ulong lastConfirmed = 5,
            ulong startHeight = 0,
            Func<ulong, BlockInformation> blockFactory = null)
        {
            Func<ulong, BlockInformation> defaultBlockFactory = h => CreateBlock(h);

            blockFactory = blockFactory ?? defaultBlockFactory;

            _lastProcessed = lastProcessed;
            _trackingSettings = new TrackingSettings { ConfirmationLimit = 0, StartHeight = startHeight, BtcNetwork = _network.Name };
            _log = new LogToMemory();

            _settingsRepository = new Mock<ISettingsRepository>();

            // get _lastProcessed
            _settingsRepository
                .Setup(m => m.GetLastProcessedBlockHeightAsync())
                .Returns(() => Task.FromResult(_lastProcessed));

            // set _lastProcessed
            _settingsRepository
                .Setup(m => m.UpdateLastProcessedBlockHeightAsync(It.IsAny<ulong>()))
                .Callback((ulong h) => _lastProcessed = h)
                .Returns(() => Task.CompletedTask);

            _commonServiceClient = new Mock<IIcoCommonServiceClient>();

            _blockchainReader = new Mock<IBlockchainReader>();

            // get last confirmed block with Height == lastConfirmed argument
            _blockchainReader
                .Setup(m => m.GetLastConfirmedBlockAsync(It.IsAny<ulong>()))
                .Returns(() => Task.FromResult(new BlockInformation
                {
                    AdditionalInformation = new BlockAdditionalInformation
                    {
                        BlockId = lastConfirmed.ToString(),
                        BlockTime = DateTimeOffset.UtcNow,
                        Height = lastConfirmed,
                        Confirmations = _trackingSettings.ConfirmationLimit
                    }
                }));

            // use factory to get block by height,
            // block with one transaction is created by default for any height
            _blockchainReader
                .Setup(m => m.GetBlockByHeightAsync(It.IsAny<ulong>()))
                .Returns((ulong h) => Task.FromResult(blockFactory(h)));

            // use factory to get block by id,
            // block with one transaction is created by default for any id
            // next to lastProcessed block height is used
            _blockchainReader
                .Setup(m => m.GetBlockByIdAsync(It.IsAny<string>()))
                .Returns((string id) =>
                {
                    var block = blockFactory(lastProcessed + 1);
                    block.AdditionalInformation.BlockId = id;
                    return Task.FromResult(block);
                });

            return new TransactionTrackingService(
                _log,
                _trackingSettings,
                _settingsRepository.Object,
                _blockchainReader.Object,
                _commonServiceClient.Object);
        }

        public BlockInformation CreateBlock(ulong height, Money money = null, Key destination = null, Script script = null)
        {
            var block = new Block();
            var blockTime = block.Header.BlockTime;
            var tx = block.AddTransaction(new Transaction());
            var amount = money ?? Money.Satoshis(1);

            if (script != null)
                tx.AddOutput(amount, script);
            else if (destination != null)
                tx.AddOutput(amount, destination);
            else
                tx.AddOutput(amount, new Key());

            var blockHash = block.GetHash().ToString();
            var blockInfo = new BlockInformation
            {
                AdditionalInformation = new BlockAdditionalInformation
                {
                    BlockId = blockHash,
                    BlockTime = blockTime,
                    Height = height,
                },
                Block = Encoders.Hex.EncodeData(block.ToBytes())
            };

            return blockInfo;
        }

        [Fact]
        public async void Track_ShouldProcessBlocksFromStartToEnd()
        {
            // Arrange
            var lastConfirmed = 5UL;
            var svc = Init(lastConfirmed: lastConfirmed);

            // Act
            await svc.Track();

            // Assert
            Assert.Equal(lastConfirmed, _lastProcessed);
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>(), It.IsAny<CancellationToken>()),
                Times.Exactly((int)lastConfirmed));
        }

        [Fact]
        public async void Track_ShouldUpdateLastProcessed()
        {
            // Arrange
            var lastProcessed = 0UL;
            var lastConfirmed = 5UL;
            var svc = Init(lastProcessed, lastConfirmed);

            // Act
            await svc.Track();

            // Assert
            Assert.Equal(lastConfirmed, _lastProcessed);
        }

        [Fact]
        public async void Track_ShouldSendCorrectMessage()
        {
            // Arrange
            var testKey = new Key();
            var testAddress = testKey.PubKey.GetAddress(_network).ToString();
            var satoshi = Money.Satoshis(1);
            var btc = satoshi.ToUnit(MoneyUnit.BTC);
            var block = CreateBlock(1, satoshi, testKey);
            var uniqueId = Block.Parse(block.Block).Transactions.First().Outputs.AsCoins().First().Outpoint.ToString();
            var transactionId = Block.Parse(block.Block).Transactions.First().Outputs.AsCoins().First().Outpoint.Hash.ToString();
            var svc = Init(
                lastProcessed: 0,
                lastConfirmed: 1,
                blockFactory: h => block);

            // Act
            await svc.Track();

            // Assert
            _commonServiceClient.Verify(m => m.HandleTransactionsAsync(
                It.Is<IList<TransactionModel>>(list => list.Any(msg =>
                    msg.Amount == btc &&
                    msg.BlockId == block.AdditionalInformation.BlockId &&
                    msg.CreatedUtc == block.AdditionalInformation.BlockTime.UtcDateTime &&
                    msg.Currency == CurrencyType.BTC &&
                    msg.PayInAddress == testAddress &&
                    msg.TransactionId == transactionId &&
                    msg.UniqueId == uniqueId)),
                It.IsAny<CancellationToken>()));
        }

        [Fact]
        public async void Track_ShouldThrow_IfBlockchainReaderThrows()
        {
            // Arrange
            var svc = Init(blockFactory: h => throw new Exception());

            // Act
            // Assert
            await Assert.ThrowsAnyAsync<Exception>(async () => await svc.Track());
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfBlockNotFound()
        {
            // Arrange
            var svc = Init(blockFactory: h => null);

            // Act
            await svc.Track();

            // Assert
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfDestinationAddressIsNullOrEmpty()
        {
            // Arrange
            var svc = Init(blockFactory: h => CreateBlock(h, script: Script.Empty));

            // Act
            await svc.Track();

            // Assert
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfThereIsNoNewData()
        {
            // Arrange
            var svc = Init(1, 1);
            
            // Act
            await svc.Track();

            // Assert
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async void ProcessBlockByHeight_ShouldSendMessage()
        {
            // Arrange
            var svc = Init(); ;

            // Act
            await svc.ProcessBlockByHeight(1);

            // Assert
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>(), It.IsAny<CancellationToken>()),
                Times.Exactly(1));
        }

        [Fact]
        public async void ProcessBlockById_ShouldSendMessage()
        {
            // Arrange
            var testBlockHash = "testBlock";
            var svc = Init();

            // Act
            await svc.ProcessBlockById(testBlockHash);

            // Assert
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>(), It.IsAny<CancellationToken>()),
                Times.Exactly(1));
        }

        [Fact]
        public async void ProcessRange_ShouldThrow_IfRangeIsInvalid()
        {
            // Arrange
            var svc = Init();

            // Act
            // Assert
            await Assert.ThrowsAnyAsync<Exception>(async () => await svc.ProcessRange(2, 1));
        }

        [Fact]
        public async void ProcessRange_ShouldSendMessage()
        {
            // Arrange
            var svc = Init();

            // Act
            await svc.ProcessRange(5, 10);

            // Assert
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>(), It.IsAny<CancellationToken>()),
                Times.Exactly(6));
        }

        [Fact]
        public async void ProcessRange_ShouldSendMessage_IfRangeIsSingleItem()
        {
            // Arrange
            var svc = Init();

            // Act
            await svc.ProcessRange(5, 5);

            // Assert
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>(), It.IsAny<CancellationToken>()),
                Times.Exactly(1));
        }
    }
}
