using System;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;

namespace Lykke.Job.IcoBtcTransactionTracker.PeriodicalHandlers
{
    public class TransactionTrackingHandler : TimerPeriod
    {
        private ILog _log;
        private IHealthService _healthService;

        private ITransactionTrackingService _trackingService;

        public TransactionTrackingHandler(int period, ILog log, IHealthService healthService, ITransactionTrackingService trackingService) : base(nameof(TransactionTrackingHandler), period, log)
        {
            _log = log;
            _healthService = healthService;
            _trackingService = trackingService;
        }

        public override async Task Execute()
        {
            _healthService.TransactionTrackingStarted();

            await _trackingService.Execute();

            _healthService.TransactionTrackingCompleted();
        }
    }
}