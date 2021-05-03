using NLog;
using PuppeteerSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Discord.WebSocket;
using System.Collections.Generic;
using Discord;

namespace StoreCheckoutBot.SiteCrawlers
{
    public class AmazonCrawler : PageCrawlerBase
    {
        public AmazonCrawler(BotSettings botSettings, StoreDetails storeDetails, ProductDetails productDetails, ProductPage productPage, Browser browser, Logger logger, DiscordSocketClient discordClient)
            : base(botSettings, storeDetails, productDetails, productPage, browser, logger, discordClient)
        {

        }

        private static object _cookieLock = new object();
        private static Task _captchaTask = Task.CompletedTask;

        public override async Task CrawlPageAsync(CancellationToken token)
        {
            try
            {
                _currentPage = await _browser.NewPageAsync();
                var productTitle = "";

                while (true)
                {
                    if (token.IsCancellationRequested)
                    {
                        _logger.Info($"Task {Id} exiting loop because the task was cancelled.");
                        return;
                    }

                    await _currentPage.GoToAsync(_productPageDetails.Url);

                    // Always check for captcha first
                    var captchaElement = await _currentPage.QuerySelectorAsync("#captchacharacters");
                    if (captchaElement != null)
                    {
                        // Ensure another page hasn't already prompted the user to complete captcha for this site
                        var basePageUri = new Uri("https://www.amazon.com");
                        var siteCookies = await _currentPage.GetCookiesAsync(basePageUri.ToString());
                        var captchaCookie = siteCookies.FirstOrDefault(x => x.Domain == basePageUri.Host && x.Name == "CaptchaInitiated");

                        if (captchaCookie != null)
                        {
                            _logger.Info($"Captcha already initiated by task {captchaCookie.Value} | {Id}");
                            if (!_captchaTask.IsCompleted)
                                await _captchaTask;

                            continue;
                        }

                        lock (_cookieLock)
                        {
                            // Make sure another page didn't just set a cookie
                            siteCookies = _currentPage.GetCookiesAsync(basePageUri.ToString()).Result;
                            captchaCookie = siteCookies.FirstOrDefault(x => x.Domain == basePageUri.Host && x.Name == "CaptchaInitiated");
                            if (captchaCookie != null)
                                continue;

                            _logger.Info($"Setting captcha cookie. | {Id}");
                            _currentPage.SetCookieAsync(new CookieParam()
                            {
                                Name = "CaptchaInitiated",
                                Value = Id.ToString(),
                                Url = basePageUri.ToString()
                            }).Wait();

                            // Now set the cookie in memory, because we'll need it later to delete it
                            siteCookies = _currentPage.GetCookiesAsync(basePageUri.ToString()).Result;
                            captchaCookie = siteCookies.First(x => x.Domain == basePageUri.Host && x.Name == "CaptchaInitiated");

                            _captchaTask = PromptUserForCaptcha();
                        }

                        try
                        {
                            await _captchaTask;
                        }
                        finally
                        {
                            _currentPage.DeleteCookieAsync(captchaCookie).Wait();
                        }
                        

                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(productTitle))
                    {
                        productTitle = await (await _currentPage.QuerySelectorAsync("#productTitle")).EvaluateFunctionAsync<string>("element => element.innerText");
                        if (productTitle.Length > 100)
                            productTitle = productTitle.Substring(0, 100);
                    }

                    _logger.Info($"Crawling for {productTitle} | {Id}");

                    var priceElement = await _currentPage.QuerySelectorAsync("#price_inside_buybox");
                    if (priceElement == null)
                    {
                        // Can't find a price, check availability
                        var availabilityElement = await _currentPage.QuerySelectorAsync("#availability");
                        if (availabilityElement != null)
                        {
                            var availabilityText = await availabilityElement.EvaluateFunctionAsync<string>("element => element.innerText");
                            if (availabilityText.Contains("Currently unavailable"))
                                _logger.Info($"{productTitle} remains unavailable | {Id}");

                            await Task.Delay((_productPageDetails.RefreshIntervalSeconds + new Random().Next(-1, 1)) * 1000);
                            continue;
                        }

                        // This is bizarre, dont' keep checking this page
                        await TakeScreenshotAsync(Path.Combine(_botSettings.ScreenshotFolderLocation, $"no_known_availability_{DateTime.Now.Ticks}.png"));
                        throw new Exception($"Could not detect a price. And no info on availability. See screenshot. Killing this thread. | {Id}");
                    }

                    var currentPriceString = await priceElement.EvaluateFunctionAsync<string>("element => element.innerText");
                    if (decimal.TryParse(currentPriceString.Replace("$", ""), out decimal currentPrice) && currentPrice <= _productDetails.MaxPrice)
                    {
                        await _currentPage.BringToFrontAsync(); // For some reason I've found more success focusing on the tab when we get to this point

                        _logger.Info($"{currentPriceString} is within price for {productTitle}! Adding to cart | {Id}");
                        await _currentPage.ClickAsync("#add-to-cart-button");
                        await _currentPage.WaitForNavigationAsync();

                        _logger.Info($"Navigating to checkout page | {Id}");
                        await _currentPage.GoToAsync("https://www.amazon.com/gp/buy/spc/handlers/display.html?hasWorkingJavascript=1"); // checkout
                        await Task.Delay(500);

                        if (_currentPage.Url.Contains("amazon.com/gp/yourstore"))
                            await _currentPage.GoToAsync("https://www.amazon.com/gp/buy/spc/handlers/display.html?hasWorkingJavascript=1"); // try again, for some reason it rarely works the first time

                        // Do some final validation checks
                        _logger.Info($"Verifying no last minute price changes. | {Id}");
                        var finalPriceElement = await _currentPage.QuerySelectorAsync(".grand-total-price");
                        if (finalPriceElement == null)
                        {
                            _logger.Info($"Unable to find the final price, see screenshot below. | {Id}");
                            await TakeScreenshotAsync(Path.Combine(_botSettings.ScreenshotFolderLocation, $"missing_final_price_{DateTime.Now.Ticks}.png"));
                            continue; // Immediately continue, don't sleep, the item is available
                        }

                        var finalPriceString = await finalPriceElement.EvaluateFunctionAsync<string>("element => element.innerText");
                        if (string.IsNullOrWhiteSpace(finalPriceString) || !decimal.TryParse(finalPriceString.Trim().Replace("$", ""), out decimal finalPrice) || finalPrice > _productDetails.MaxPrice)
                        {
                            _logger.Warn($"Final price of {finalPriceString} was more expensive than the given MaxPrice for this product ({_productDetails.MaxPrice}). See screenshot. | {Id}");
                            await TakeScreenshotAsync(Path.Combine(_botSettings.ScreenshotFolderLocation, $"final_price_discrepency_{DateTime.Now.Ticks}.png"));
                            await Task.Delay((_productPageDetails.RefreshIntervalSeconds + new Random().Next(-1, 1)) * 1000);
                            continue;
                        }

                        if (!token.IsCancellationRequested)
                        {
                            await _currentPage.ClickAsync("#submitOrderButtonId input");
                            await Task.Delay(5000);
                            _logger.Info($"Successfully purchased {productTitle}! See checkout screenshot for proof. | {Id}");
                            await TakeScreenshotAsync(Path.Combine(_botSettings.ScreenshotFolderLocation, $"purchase_successful_{DateTime.Now.Ticks}.png"));
                        }

                        return;
                    }
                    else
                    {
                        _logger.Info($"{productTitle} too expensive ({currentPriceString}), continuing | {Id}");
                        await Task.Delay((_productPageDetails.RefreshIntervalSeconds + new Random().Next(-1, 1)) * 1000);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"An unknown exception occurred while crawling the page {_productPageDetails.Url}.");
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

        public async Task PromptUserForCaptcha()
        {
            _logger.Info($"Initiating captcha prompt to user | {Id}");

            var chatBotServer = _discordClient.Guilds.FirstOrDefault(x => x.Name == _botSettings.DiscordBotServerName);
            if (chatBotServer == null)
                throw new Exception($"ERROR Unable to find discord server named {_botSettings.DiscordBotServerName}");

            var storeChannel = chatBotServer.TextChannels.FirstOrDefault(x => x.Name == _storeDetails.Name.ToLower());
            if (storeChannel == null)
            {
                await chatBotServer.CreateTextChannelAsync(_storeDetails.Name);
                var channels = chatBotServer.TextChannels.ToList();
                storeChannel = chatBotServer.TextChannels.First(x => x.Name == _storeDetails.Name.ToLower());
            }

            var users = chatBotServer.Users;
            var userToNotify = storeChannel.Users.FirstOrDefault(x => x.Username == _botSettings.UserDiscordName);
            if (userToNotify == null)
                throw new Exception($"ERROR Unable to find a user with the nickname {userToNotify} on the channel {storeChannel.Name}.");

            while (true)
            {
                var captchaSuccess = false;
                _logger.Info("Taking screenshot of Captcha");
                var captchaFileName = Path.Combine(_botSettings.ScreenshotFolderLocation, $"{Guid.NewGuid()}.png");
                await TakeScreenshotAsync(captchaFileName);

                var messageId = (await storeChannel.SendFileAsync(captchaFileName, $"{userToNotify.Mention} please complete the captcha")).Id;
                while (true)
                {
                    // Wait for user input. Just grab the most recent message the user sent
                    var userMessagesPages = await storeChannel.GetMessagesAsync(messageId, Direction.After).ToListAsync();
                    var userMessages = userMessagesPages.SelectMany(x => x.ToList());
                    var mostRecentUserMessage = userMessages.Where(x => x.Author == userToNotify).OrderByDescending(x => x.Timestamp).FirstOrDefault();

                    if (mostRecentUserMessage == null)
                    {
                        await Task.Delay(10000);
                        continue;
                    }

                    messageId = mostRecentUserMessage.Id; // Set the message we're checking around to the most recent message we processed

                    // Parse the user input and enter the captcha
                    if (!string.IsNullOrWhiteSpace(mostRecentUserMessage.Content))
                    {
                        await _currentPage.WaitForSelectorAsync("#captchacharacters");
                        await _currentPage.FocusAsync("#captchacharacters");
                        await _currentPage.Keyboard.TypeAsync(mostRecentUserMessage.Content);
                        await _currentPage.ClickAsync("button");

                        try
                        {
                            await _currentPage.WaitForNavigationAsync(new NavigationOptions() { Timeout = 5 });
                        }
                        catch (Exception)
                        { 
                            // just ignore the timeout, we just don't want the page stalling forever
                        }
                        
                        captchaSuccess = (await _currentPage.QuerySelectorAsync("#captchacharacters")) == null;
                        break;
                    }
                }

                if (captchaSuccess)
                    break;

                _logger.Info("Captcha input was not successful. Taking another screenshot and trying again.");
            }
        }

        private async Task TakeScreenshotAsync(string fileName)
        {
            await _currentPage.SetViewportAsync(new ViewPortOptions
            {
                Width = _botSettings.ScreenshotWidth,
                Height = _botSettings.ScreenshotHeight
            });

            await _currentPage.ScreenshotAsync(fileName);
        }
    }
}
