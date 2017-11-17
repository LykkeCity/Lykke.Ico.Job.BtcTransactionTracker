using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.WindowsAzure.Storage.Table;

namespace Lykke.Job.IcoBtcTransactionTracker.AzureRepositories
{
    public class Address : TableEntity
    {
        public Address()
        {
        }

        public Address(string currency, string address)
        {
            PartitionKey = currency;
            RowKey = address;
        }

        public string Email { get; set; }
    }
}
