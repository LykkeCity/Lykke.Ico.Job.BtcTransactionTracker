using Autofac;
using Common.Log;
using Lykke.Job.IcoBtcTransactionTracker.AzureRepositories.Settings;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Settings;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;
using Lykke.Job.IcoBtcTransactionTracker.Core.Settings.JobSettings;
using Lykke.Job.IcoBtcTransactionTracker.PeriodicalHandlers;
using Lykke.Job.IcoBtcTransactionTracker.Services;
using Lykke.Service.IcoCommon.Client;
using Lykke.SettingsReader;

namespace Lykke.Job.IcoBtcTransactionTracker.Modules
{
    public class JobModule : Module
    {
        private readonly IcoBtcTransactionTrackerSettings _settings;
        private readonly IReloadingManager<DbSettings> _dbSettingsManager;
        private readonly ILog _log;

        public JobModule(
            IcoBtcTransactionTrackerSettings settings, 
            IReloadingManager<DbSettings> dbSettingsManager,
            ILog log)
        {
            _settings = settings;
            _log = log;
            _dbSettingsManager = dbSettingsManager;
        }

        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterInstance(_log)
                .As<ILog>()
                .SingleInstance();

            builder.RegisterType<HealthService>()
                .As<IHealthService>()
                .SingleInstance();

            builder.RegisterType<StartupManager>()
                .As<IStartupManager>();

            builder.RegisterType<ShutdownManager>()
                .As<IShutdownManager>();

            builder.RegisterIcoCommonClient(_settings.CommonServiceUrl, _log);

            builder.RegisterType<SettingsRepository>()
                .As<ISettingsRepository>()
                .WithParameter(TypedParameter.From(_dbSettingsManager.ConnectionString(x => x.DataConnString)))
                .WithParameter(TypedParameter.From(_settings.InstanceId));

            builder.RegisterType<BlockchainReader>()
                .As<IBlockchainReader>()
                .WithParameter(TypedParameter.From(_settings.Tracking.BtcUrl));

            builder.RegisterType<TransactionTrackingService>()
                .As<ITransactionTrackingService>()
                .WithParameter(TypedParameter.From(_settings.Tracking));

            RegisterPeriodicalHandlers(builder);
        }

        private void RegisterPeriodicalHandlers(ContainerBuilder builder)
        {
            builder.RegisterType<TransactionTrackingHandler>()
                .As<IStartable>()
                .AutoActivate()
                .WithParameter(TypedParameter.From(_settings.TrackingInterval))
                .SingleInstance();
        }
    }
}
