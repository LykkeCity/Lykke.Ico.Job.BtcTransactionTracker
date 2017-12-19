using System;
using System.Collections.Generic;
using System.Text;

namespace Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain
{
    public class BlockInformation
    {
        public BlockAdditionalInformation AdditionalInformation { get; set; }
        public string Block { get; set; }
    }
}
