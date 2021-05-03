using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using NLog.Web;
using PuppeteerSharp;
using StoreCheckoutBot.SiteCrawlers;
using Discord;
using Discord.WebSocket;

namespace StoreCheckoutBot
{
    class Program
    {
        private static Configuration _config { get; set; } = new Configuration();
        private static NLog.Logger _logger { get; set; }

        private static DiscordSocketClient _discordClient { get; set; } = new DiscordSocketClient(new DiscordSocketConfig { AlwaysDownloadUsers = true });
        private static bool IsDiscordReady { get; set; } = false;

        static async Task Main(string[] args)
        {
            new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build()
                .Bind(_config); // Map to strongly typed config object

            _logger = NLogBuilder.ConfigureNLog("nlog.config").GetCurrentClassLogger();

            _discordClient.Log += HandleDiscordLogs; // Hook up the log event to our application logger
            _discordClient.Ready += HandleDiscordReady;

            await _discordClient.LoginAsync(TokenType.Bot, _config.BotSettings.DiscordBotToken);
            await _discordClient.StartAsync();
            while (!IsDiscordReady)
            { 
                // wait for ready event to fire
            }


            try
            {
                _logger.Info("Starting Crawler");
                _logger.Info("Downloading the latest version of Chromium");
                await new BrowserFetcher().DownloadAsync();

                // Set up the browser
                Browser browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = _config.BotSettings.UseHeadlessBrowser
                });

                var allProductCrawlers = new List<Task>();
                var allCrawlerTypes = Assembly.GetAssembly(typeof(PageCrawlerBase)).GetTypes().Where(myType => myType.IsClass && !myType.IsAbstract && myType.IsSubclassOf(typeof(PageCrawlerBase))).ToList();
                foreach (var store in _config.StoreConfigs)
                {
                    try
                    {
                        // Verify we built page crawlers for this site
                        var crawlerType = allCrawlerTypes.FirstOrDefault(x => x.Name == $"{store.StoreDetails.Name}Crawler");
                        if (crawlerType == null)
                        {
                            _logger.Warn($"Unable to find a site crawler for the store {store.StoreDetails.Name}. If you are sure this store is supported, please check the spelling of the store in the config file.");
                            continue;
                        }

                        // Create a stub instance for this site and login
                        await ((PageCrawlerBase)Activator.CreateInstance(crawlerType, new object[7] { _config.BotSettings, store.StoreDetails, null, null, browser, _logger, _discordClient })).LoginAsync();

                        // Iterate through the pages and create a crawler for each
                        foreach (var product in store.Products)
                        {
                            var productPageCrawlers = new List<PageCrawlerBase>();
                            foreach (var page in product.ProductPages)
                            {
                                var pageCrawler = (PageCrawlerBase)Activator.CreateInstance(crawlerType, new object[7] { _config.BotSettings, store.StoreDetails, product.ProductDetails, page, browser, _logger, _discordClient });
                                productPageCrawlers.Add(pageCrawler);
                            }

                            // Begin crawling and store the store task in a global array
                            allProductCrawlers.Add(CrawlForProduct(productPageCrawlers));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"An error occurred while attempting to crawl the store {store.StoreDetails.Name}.");
                    }
                }

                // Wait for all products to finish crawling
                await Task.WhenAll(allProductCrawlers);

                _logger.Info("All products purchased. Shutting down program");
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Stopped program because of exception");
                throw;
            }
            finally
            {
                // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
                NLog.LogManager.Shutdown();
            }
        }

        private static Task _discordClient_Log(LogMessage arg)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Initializes crawling for each page passed in, and returns a Task where success indicates one of the pages was successful
        /// in purchasing a product. Once successful, it cancels the rest of the pages so as not to buy duplicates.
        /// </summary>
        static async Task CrawlForProduct(List<PageCrawlerBase> crawlers)
        {
            // Initialize a token source we can use to manage only purchasing one item per product
            var tokenSource = new CancellationTokenSource();
            var cancellationToken = tokenSource.Token;

            var crawlingTasks = crawlers.ConvertAll(x => x.CrawlPageAsync(cancellationToken));

            try
            {
                while (crawlingTasks.Count > 0)
                {
                    var finishedTask = await Task.WhenAny(crawlingTasks);
                    if (finishedTask.IsCompletedSuccessfully)
                    { 
                        tokenSource.Cancel(); // Kill all the other pages looking for this product
                        crawlingTasks.Clear();
                    }
                    else
                    {
                        crawlingTasks.Remove(finishedTask); // an exception occurred, keep waiting for any of the other pages
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurred while waiting for product pages to crawl.");
                throw ex;
            }
        }

        private static Task HandleDiscordLogs(LogMessage message)
        {
            _logger.Debug($"DISCORD: {message.Message}");
            return Task.CompletedTask;
        }

        private static Task HandleDiscordReady()
        {
            IsDiscordReady = true;
            return Task.CompletedTask;
        }
    }
}
