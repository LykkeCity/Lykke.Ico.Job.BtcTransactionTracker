using System;
using System.Linq;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Ico.Core;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Ico.Core.Repositories.CampaignInfo;
using Lykke.Ico.Core.Repositories.InvestorAttribute;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;
using Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings;
using Lykke.Job.IcoBtcTransactionTracker.Services;
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
        private Mock<ICampaignInfoRepository> _campaignInfoRepository;
        private Mock<IInvestorAttributeRepository> _investorAttributeRepository;
        private Mock<IQueuePublisher<TransactionMessage>> _transactionQueue;
        private Mock<IBlockchainReader> _blockchainReader;
        private Network _network = Network.TestNet;
        private string _lastProcessed;

        public string LastProcessed
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

            _lastProcessed = lastProcessed.ToString();
            _trackingSettings = new TrackingSettings { ConfirmationLimit = 0, StartHeight = startHeight, BtcNetwork = _network.Name };
            _log = new LogToMemory();

            _campaignInfoRepository = new Mock<ICampaignInfoRepository>();

            // get _lastProcessed
            _campaignInfoRepository
                .Setup(m => m.GetValueAsync(It.Is<CampaignInfoType>(t => t == CampaignInfoType.LastProcessedBlockBtc)))
                .Returns(() => Task.FromResult(_lastProcessed));

            // set _lastProcessed
            _campaignInfoRepository
                .Setup(m => m.SaveValueAsync(It.Is<CampaignInfoType>(t => t == CampaignInfoType.LastProcessedBlockBtc), It.IsAny<string>()))
                .Callback((CampaignInfoType t, string v) => _lastProcessed = v)
                .Returns(() => Task.CompletedTask);

            _investorAttributeRepository = new Mock<IInvestorAttributeRepository>();

            // return test email for any pay-in address
            _investorAttributeRepository
                .Setup(m => m.GetInvestorEmailAsync(
                    It.IsIn(new InvestorAttributeType[] { InvestorAttributeType.PayInBtcAddress, InvestorAttributeType.PayInEthAddress }),
                    It.IsAny<string>()))
                .Returns(() => Task.FromResult("test@test.test"));

            _transactionQueue = new Mock<IQueuePublisher<TransactionMessage>>();

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
                _campaignInfoRepository.Object,
                _investorAttributeRepository.Object,
                _transactionQueue.Object,
                _blockchainReader.Object);
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
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Exactly((int)lastConfirmed));
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
            Assert.Equal(lastConfirmed.ToString(), _lastProcessed);
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
            _transactionQueue.Verify(m => m.SendAsync(It.Is<TransactionMessage>(msg =>
                msg.Amount == btc &&
                msg.BlockId == block.AdditionalInformation.BlockId &&
                msg.CreatedUtc == block.AdditionalInformation.BlockTime.UtcDateTime &&
                msg.Currency == CurrencyType.Bitcoin &&
                msg.PayInAddress == testAddress &&
                msg.TransactionId == transactionId &&
                msg.UniqueId == uniqueId)));
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
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Never);
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfDestinationAddressIsNullOrEmpty()
        {
            // Arrange
            var svc = Init(blockFactory: h => CreateBlock(h, script: Script.Empty));

            // Act
            await svc.Track();

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Never);
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfInvestorEmailNotFound()
        {
            // Arrange
            var svc = Init();
            _investorAttributeRepository
                .Setup(m => m.GetInvestorEmailAsync(It.IsAny<InvestorAttributeType>(), It.IsAny<string>()))
                .Returns(() => Task.FromResult<string>(null));

            // Act
            await svc.Track();

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Never);
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfThereIsNoNewData()
        {
            // Arrange
            var svc = Init(1, 1);
            
            // Act
            await svc.Track();

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Never);
        }

        [Fact]
        public async void ProcessBlockByHeight_ShouldSendMessage()
        {
            // Arrange
            var svc = Init(); ;

            // Act
            await svc.ProcessBlockByHeight(1);

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Exactly(1));
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
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Exactly(1));
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
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Exactly(6));
        }

        [Fact]
        public async void ProcessRange_ShouldSendMessage_IfRangeIsSingleItem()
        {
            // Arrange
            var svc = Init();

            // Act
            await svc.ProcessRange(5, 5);

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Exactly(1));
        }
    }
}
