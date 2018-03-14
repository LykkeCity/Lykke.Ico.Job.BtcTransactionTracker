using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Job.IcoBtcTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoBtcTransactionTracker.Core.Services;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;

namespace Lykke.Job.IcoBtcTransactionTracker.Services
{
    public class BlockchainReader : IBlockchainReader
    {
        private readonly ILog _log;
        private readonly RetryPolicy<HttpResponseMessage> _retry;
        private HttpClient _ninjaClient;
        private void ReinitClient(string url) => 
            _ninjaClient = new HttpClient() { BaseAddress = new Uri(url.TrimEnd('/')) };

        public BlockchainReader(ILog log, string ninjaUrl)
        {
            _log = log;
            _retry = Policy
                .Handle<TaskCanceledException>()
                .OrResult<HttpResponseMessage>(res => res.StatusCode == HttpStatusCode.RequestTimeout || 
                    (res.StatusCode >= HttpStatusCode.InternalServerError && res.StatusCode != HttpStatusCode.NotImplemented))
                .WaitAndRetryAsync(5,
                    i => TimeSpan.FromMilliseconds(Math.Pow(2, i) * 50),
                    (res, _) =>
                    {
                        if (res.Exception is TaskCanceledException)
                        {
                            // there is a problem with singleton HttpClient in .net Core - https://github.com/dotnet/corefx/issues/25800
                            // so it's kinda better to re-create HttpClient in case of TaskCanceledException
                            ReinitClient(ninjaUrl);
                        }
                    });

            ReinitClient(ninjaUrl);
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
            var resp = await _retry.ExecuteAsync(() => _ninjaClient.GetAsync(url));

            if (resp.StatusCode == HttpStatusCode.NotFound && new Regex("(.+)(?: not found)$").IsMatch(resp.ReasonPhrase))
            {
                // 404 is lawful if it's accompanied by scpecification of not found object 
                await _log.WriteWarningAsync(nameof(DoNinjaRequest), $"Url: {url}", resp.ReasonPhrase);
                return null;
            }

            resp.EnsureSuccessStatusCode();

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
