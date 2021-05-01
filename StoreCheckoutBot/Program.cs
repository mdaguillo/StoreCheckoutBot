﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using NLog.Web;
using PuppeteerSharp;
using StoreCheckoutBot.SiteCrawlers;

namespace StoreCheckoutBot
{
    class Program
    {
        private static Configuration _config { get; set; } = new Configuration();
        private static NLog.Logger _logger { get; set; }

        static async Task Main(string[] args)
        {
            new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build()
                .Bind(_config); // Map to strongly typed config object

            _logger = NLogBuilder.ConfigureNLog("nlog.config").GetCurrentClassLogger();

            try
            {
                _logger.Info("Starting Crawler");
                _logger.Info("Downloading the latest version of Chromium");
                await new BrowserFetcher().DownloadAsync();

                // Set up the browser
                Browser browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = _config.UseHeadlessBrowser
                });

                var allProductCrawlers = new List<Task>();
                var allCrawlerTypes = Assembly.GetAssembly(typeof(PageCrawlerBase)).GetTypes().Where(myType => myType.IsClass && !myType.IsAbstract && myType.IsSubclassOf(typeof(PageCrawlerBase))).ToList();
                foreach (var store in _config.StoreConfigs)
                {
                    try
                    {
                        // Verify we built page crawlers for this site
                        var crawlerType = allCrawlerTypes.FirstOrDefault(x => x.Name == $"{store.Name}Crawler");
                        if (crawlerType == null)
                        {
                            _logger.Warn($"Unable to find a site crawler for the store {store.Name}. If you are sure this store is supported, please check the spelling of the store in the config file.");
                            continue;
                        }

                        // Create a stub instance for this site and login
                        await ((PageCrawlerBase)Activator.CreateInstance(crawlerType, new object[10] { "", store.Username, store.Password, _config.ScreenshotFolderLocation, _config.ScreenshotWidth, _config.ScreenshotHeight, (decimal)0.0, (int)0, browser, _logger })).LoginAsync();

                        // Iterate through the pages and create a crawler for each
                        foreach (var product in store.Products)
                        {
                            var productPageCrawlers = new List<PageCrawlerBase>();
                            foreach (var page in product.ProductPages)
                            {
                                var pageCrawler = (PageCrawlerBase)Activator.CreateInstance(crawlerType, new object[10] { page.Url, store.Username, store.Password, _config.ScreenshotFolderLocation, _config.ScreenshotWidth, _config.ScreenshotHeight, product.MaxPrice, page.RefreshIntervalSeconds, browser, _logger });
                                productPageCrawlers.Add(pageCrawler);
                            }

                            // Begin crawling and store the store task in a global array
                            allProductCrawlers.Add(CrawlForProduct(productPageCrawlers));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"An error occurred while attempting to crawl the store {store.Name}.");
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
    }
}
