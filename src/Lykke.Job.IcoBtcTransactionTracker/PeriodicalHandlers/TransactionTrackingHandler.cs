﻿using System;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;

namespace Lykke.Job.IcoBtcTransactionTracker.PeriodicalHandlers
{
    public class TransactionTrackingHandler : TimerPeriod
    {
        private ILog _log;
        private ITransactionTrackingService _trackingService;

        public TransactionTrackingHandler(int trackingInterval, ILog log, ITransactionTrackingService trackingService) : 
            base(nameof(TransactionTrackingHandler), trackingInterval, log)
        {
            _log = log;
            _trackingService = trackingService;
        }

        public override async Task Execute()
        {
            try
            {
                await _trackingService.Track();
            }
            catch (Exception ex)
            {
                await _log.WriteErrorAsync(nameof(Execute), null, ex);
            }
        }
    }
}
