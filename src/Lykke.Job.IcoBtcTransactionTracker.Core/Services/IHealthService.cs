using System.Collections.Generic;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Health;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Services
{
    // NOTE: See https://lykkex.atlassian.net/wiki/spaces/LKEWALLET/pages/35755585/Add+your+app+to+Monitoring
    public interface IHealthService
    {
        string GetHealthViolationMessage();
        IEnumerable<HealthIssue> GetHealthIssues();
        void TransactionTrackingStarted();
        void TransactionTrackingCompleted();

        // TODO: Place health tracing methods declarations here
    }
}