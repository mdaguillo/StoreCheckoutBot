using NLog;
using PuppeteerSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StoreCheckoutBot.SiteCrawlers
{
    public class AmazonCrawler : PageCrawlerBase
    {
        public AmazonCrawler(BotSettings botSettings, StoreDetails storeDetails, ProductDetails productDetails, ProductPage productPage, Browser browser, Logger logger) 
            : base(botSettings, storeDetails, productDetails, productPage, browser, logger)
        {

        }

        public override async Task CrawlPageAsync(CancellationToken token)
        {
            try 
            {
                var taskId = Guid.NewGuid();
                var productPage = await _browser.NewPageAsync();
                var productTitle = "";

                while (true)
                {
                    if (token.IsCancellationRequested)
                    {
                        _logger.Info($"Task {taskId} exiting loop because the task was cancelled.");
                        return;
                    }

                    await productPage.GoToAsync(_productPage.Url);
                    if (string.IsNullOrWhiteSpace(productTitle))
                    {
                        productTitle = await (await productPage.QuerySelectorAsync("#productTitle")).EvaluateFunctionAsync<string>("element => element.innerText");
                        if (productTitle.Length > 100) 
                            productTitle = productTitle.Substring(0, 100);
                    }

                    _logger.Info($"Crawling for {productTitle} | {taskId}");

                    var priceElement = await productPage.QuerySelectorAsync("#price_inside_buybox");
                    if (priceElement == null)
                    {
                        // Can't find a price, check availability
                        var availabilityElement = await productPage.QuerySelectorAsync("#availability");
                        if (availabilityElement == null)
                        {
                            // This is bizarre, dont' keep checking this page
                            await TakeScreenshotAsync(productPage, Path.Combine(_botSettings.ScreenshotFolderLocation, $"no_known_availability_{DateTime.Now.Ticks}.png"));
                            throw new Exception($"Could not detect a price. And no info on availability. See screenshot. Killing this thread. | {taskId}");
                        }

                        var availabilityText = await availabilityElement.EvaluateFunctionAsync<string>("element => element.innerText");
                        if (availabilityText.Contains("Currently unavailable"))
                            _logger.Info($"{productTitle} remains unavailable | {taskId}");

                        await Task.Delay((_productPage.RefreshIntervalSeconds + new Random().Next(-1, 1)) * 1000);
                        continue;
                    }

                    var currentPriceString = await priceElement.EvaluateFunctionAsync<string>("element => element.innerText");
                    _logger.Info($"{productTitle} currently priced at {currentPriceString} | {taskId}");
                    if (decimal.TryParse(currentPriceString.Replace("$", ""), out decimal currentPrice) && currentPrice <= _productDetails.MaxPrice)
                    {
                        await productPage.BringToFrontAsync(); // For some reason I've found more success focusing on the tab when we get to this point

                        _logger.Info($"{currentPriceString} is within price for {productTitle}! Adding to cart | {taskId}");
                        await productPage.ClickAsync("#add-to-cart-button");
                        await productPage.WaitForNavigationAsync();

                        _logger.Info($"Navigating to checkout page | {taskId}");
                        await productPage.GoToAsync("https://www.amazon.com/gp/buy/spc/handlers/display.html?hasWorkingJavascript=1"); // checkout
                        await Task.Delay(500);

                        if (productPage.Url.Contains("amazon.com/gp/yourstore"))
                            await productPage.GoToAsync("https://www.amazon.com/gp/buy/spc/handlers/display.html?hasWorkingJavascript=1"); // try again, for some reason it rarely works the first time

                        // Do some final validation checks
                        _logger.Info($"Verifying no last minute price changes. | {taskId}");
                        var finalPriceElement = await productPage.QuerySelectorAsync(".grand-total-price");
                        if (finalPriceElement == null)
                        {
                            _logger.Info($"Unable to find the final price, see screenshot below. | {taskId}");
                            await TakeScreenshotAsync(productPage, Path.Combine(_botSettings.ScreenshotFolderLocation, $"missing_final_price_{DateTime.Now.Ticks}.png"));
                            continue; // Immediately continue, don't sleep, the item is available
                        }

                        var finalPriceString = await finalPriceElement.EvaluateFunctionAsync<string>("element => element.innerText");
                        if (string.IsNullOrWhiteSpace(finalPriceString) || !decimal.TryParse(finalPriceString.Trim().Replace("$", ""), out decimal finalPrice) || finalPrice > _productDetails.MaxPrice + 100)
                        {
                            _logger.Warn($"Final price of {finalPriceString} was more expensive than the given MaxPrice plus a buffer of $100. See screenshot. | {taskId}");
                            await TakeScreenshotAsync(productPage, Path.Combine(_botSettings.ScreenshotFolderLocation, $"final_price_discrepency_{DateTime.Now.Ticks}.png"));
                            await Task.Delay((_productPage.RefreshIntervalSeconds + new Random().Next(-1, 1)) * 1000);
                            continue;
                        }

                        if (!token.IsCancellationRequested)
                        {
                            await productPage.ClickAsync("#submitOrderButtonId input");
                            await Task.Delay(5000);
                            _logger.Info($"Successfully purchased {productTitle}! See checkout screenshot for proof. | {taskId}");
                            await TakeScreenshotAsync(productPage, Path.Combine(_botSettings.ScreenshotFolderLocation, $"purchase_successful_{DateTime.Now.Ticks}.png"));
                        }

                        return;
                    }
                    else
                    {
                        _logger.Info($"{productTitle} too expensive ({currentPriceString}), continuing | {taskId}");
                        await Task.Delay((_productPage.RefreshIntervalSeconds + new Random().Next(-1, 1)) * 1000);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"An unknown exception occurred while crawling the page {_productPage.Url}.");
                throw;
            }
        }

        public override async Task LoginAsync()
        {
            Page page = await _browser.NewPageAsync();

            try
            {
                _logger.Info("Logging into Amazon");

                await page.GoToAsync("https://www.amazon.com/");
                await page.WaitForSelectorAsync("#nav-signin-tooltip a");
                await page.ClickAsync("#nav-signin-tooltip a");
                await page.WaitForSelectorAsync("#ap_email");
                await page.FocusAsync("#ap_email");
                await page.Keyboard.TypeAsync(_storeDetails.Username);
                await page.ClickAsync("#continue");
                await page.WaitForSelectorAsync("#ap_password");
                await page.FocusAsync("#ap_password");
                await page.Keyboard.TypeAsync(_storeDetails.Password);
                await page.ClickAsync("#signInSubmit");
                await page.WaitForNavigationAsync();

                // Sometimes we need to do additional login verification
                var alertElement = await page.QuerySelectorAsync("div.a-alert-content");
                if (alertElement != null)
                {
                    // Wait for user
                    _logger.Info("Additional login verification needed. Once finished please hit enter in the console.");
                    Console.ReadLine();
                }

                _logger.Info("Successfully logged into Amazon");
                await page.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to login to Amazon.");
                await page.ScreenshotAsync(Path.Combine(_botSettings.ScreenshotFolderLocation, $"failed_to_login_{DateTime.Now.Ticks}.png"));
                throw;
            }
        }

        private async Task TakeScreenshotAsync(Page page, string fileName) 
        {
            await page.SetViewportAsync(new ViewPortOptions
            {
                Width = _botSettings.ScreenshotWidth,
                Height = _botSettings.ScreenshotHeight
            });

            await page.ScreenshotAsync(fileName);
        }
    }
}
