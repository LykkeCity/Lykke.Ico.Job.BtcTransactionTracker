﻿using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;
using Newtonsoft.Json;

namespace Lykke.Job.IcoBtcTransactionTracker.Services
{
    public class BlockchainReader : IBlockchainReader
    {
        private readonly ILog _log;
        private readonly HttpClient _ninjaHttpClient = new HttpClient();

        public BlockchainReader(ILog log, string ninjaUrl)
        {
            _log = log;
            _ninjaHttpClient.BaseAddress = new Uri(ninjaUrl);
        }

        public async Task<BlockInformation> GetBlockByHeightAsync(ulong height)
        {
            return await DoNinjaRequest<BlockInformation>($"blocks/{height}");
        }

        public async Task<BlockInformation> GetBlockByIdAsync(string id)
        {
            return await DoNinjaRequest<BlockInformation>($"blocks/{id}");
        }

        public async Task<BlockInformation> GetLastConfirmedBlockAsync(ulong confirmationLimit = 0)
        {
            var url = confirmationLimit > 0 ?
                $"blocks/tip-{confirmationLimit - 1}?headeronly=true" :
                $"blocks/tip?headeronly=true";

            return await DoNinjaRequest<BlockInformation>(url);
        }

        public async Task<T> DoNinjaRequest<T>(string url) where T : class
        {
            var resp = await _ninjaHttpClient.GetAsync(url);

            try
            {
                resp.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    await _log.WriteErrorAsync(nameof(DoNinjaRequest), $"Url: {url}", ex);
                    return null;
                }
                else
                {
                    throw;
                }
            }

            var json = await resp.Content.ReadAsStringAsync();

            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (JsonSerializationException ex)
            {
                await _log.WriteErrorAsync(nameof(DoNinjaRequest), $"Url: {url}, Response: {json}", ex);
                return null;
            }
        }
    }
}
