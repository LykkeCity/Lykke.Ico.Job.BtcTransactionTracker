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
        private string _lastProcessed = string.Empty;

        private TransactionTrackingService Init(
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

            _campaignInfoRepository
                .Setup(m => m.GetValueAsync(It.Is<CampaignInfoType>(t => t == CampaignInfoType.LastProcessedBlockBtc)))
                .Returns(() => Task.FromResult(_lastProcessed));

            _campaignInfoRepository
                .Setup(m => m.SaveValueAsync(It.Is<CampaignInfoType>(t => t == CampaignInfoType.LastProcessedBlockBtc), It.IsAny<string>()))
                .Callback((CampaignInfoType t, string v) => _lastProcessed = v)
                .Returns(() => Task.CompletedTask);

            _investorAttributeRepository = new Mock<IInvestorAttributeRepository>();

            _investorAttributeRepository
                .Setup(m => m.GetInvestorEmailAsync(
                    It.IsIn(new InvestorAttributeType[] { InvestorAttributeType.PayInBtcAddress, InvestorAttributeType.PayInEthAddress }),
                    It.IsAny<string>()))
                .Returns(() => Task.FromResult("test@test.test"));

            _transactionQueue = new Mock<IQueuePublisher<TransactionMessage>>();

            _transactionQueue
                .Setup(m => m.SendAsync(It.IsAny<TransactionMessage>()))
                .Returns(() => Task.CompletedTask);

            _blockchainReader = new Mock<IBlockchainReader>();

            _blockchainReader
                .Setup(m => m.GetLastConfirmedBlockAsync(It.IsAny<ulong>()))
                .Returns(() => Task.FromResult(new BlockInformation
                {
                    AdditionalInformation = new BlockAdditionalInformation
                    {
                        Height = lastConfirmed
                    }
                }));

            _blockchainReader
                .Setup(m => m.GetBlockByHeightAsync(It.IsInRange(lastProcessed, lastConfirmed, Range.Inclusive)))
                .Returns((ulong h) => Task.FromResult(blockFactory(h)));

            return new TransactionTrackingService(
                _log,
                _trackingSettings,
                _campaignInfoRepository.Object,
                _investorAttributeRepository.Object,
                _transactionQueue.Object,
                _blockchainReader.Object);
        }

        private BlockInformation CreateBlock(ulong height, Money money = null, Key destination = null)
        {
            var block = new Block();
            var blockTime = block.Header.BlockTime;
            var tx = block.AddTransaction(new Transaction());

            tx.AddOutput(money ?? Money.Satoshis(1), destination ?? new Key());

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
        public async void ShouldProcessBlocksFromStartToEnd()
        {
            // Arrange
            var lastConfirmed = 5UL;
            var svc = Init(lastConfirmed: lastConfirmed);

            // Act
            await svc.Execute();

            // Assert
            Assert.Equal(lastConfirmed.ToString(), _lastProcessed);
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Exactly((int)lastConfirmed));
        }

        [Fact]
        public async void ShouldSendCorrectMessage()
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
            await svc.Execute();

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
    }
}
