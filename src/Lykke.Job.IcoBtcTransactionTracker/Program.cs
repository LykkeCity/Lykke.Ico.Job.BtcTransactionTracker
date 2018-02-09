﻿using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;

namespace Lykke.Job.IcoBtcTransactionTracker
{
    public class Program
    {
        public static string EnvInfo => Environment.GetEnvironmentVariable("ENV_INFO");

        public static void Main(string[] args)
        {
            Console.WriteLine($"IcoBtcTransactionTracker version {Microsoft.Extensions.PlatformAbstractions.PlatformServices.Default.Application.ApplicationVersion}");
#if DEBUG
            Console.WriteLine("Is DEBUG");
#else
            Console.WriteLine("Is RELEASE");
#endif
            Console.WriteLine($"ENV_INFO: {Environment.GetEnvironmentVariable("ENV_INFO")}");

            try
            {
                var webHost = new WebHostBuilder()
                    .UseKestrel()
                    .UseUrls("http://*:5001")
                    .UseContentRoot(Directory.GetCurrentDirectory())
                    .UseStartup<Startup>()
                    .UseApplicationInsights()
                    .Build();

                webHost.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fatal error:");
                Console.WriteLine(ex);

                // Lets devops to see startup error in console between restarts in the Kubernetes
                var delay = TimeSpan.FromMinutes(1);

                Console.WriteLine();
                Console.WriteLine($"Process will be terminated in {delay}. Press any key to terminate immediately.");

                Task.WhenAny(
                        Task.Delay(delay),
                        Task.Run(() =>
                        {
                            Console.ReadKey(true);
                        }))
                    .Wait();
            }

            Console.WriteLine("Terminated");
        }
    }
}
