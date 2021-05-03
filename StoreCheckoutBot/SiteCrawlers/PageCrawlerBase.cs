using Discord.WebSocket;
using NLog;
using PuppeteerSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StoreCheckoutBot.SiteCrawlers
{
    public abstract class PageCrawlerBase
    {
        protected string Id { get; }
        protected BotSettings _botSettings { get; set; }
        protected StoreDetails _storeDetails { get; set; }
        protected ProductDetails _productDetails { get; set; }
        protected ProductPage _productPageDetails { get; set; }
        protected Logger _logger { get; set; }
        protected Browser _browser { get; set; }
        protected DiscordSocketClient _discordClient { get; set; }
        protected Page _currentPage { get; set; }

        public PageCrawlerBase(BotSettings botSettings, StoreDetails storeDetails, ProductDetails productDetails, ProductPage productPage, Browser browser, Logger logger, DiscordSocketClient discordClient) {
            _botSettings = botSettings;
            _storeDetails = storeDetails;
            _productDetails = productDetails;
            _productPageDetails = productPage;
            _logger = logger;
            _browser = browser;
            _discordClient = discordClient;

            Id = Guid.NewGuid().ToString();
        }

        public async virtual Task CrawlPageAsync(CancellationToken token) {
            throw new NotImplementedException();
        }
        public async virtual Task LoginAsync() {
            throw new NotImplementedException();
        }
    }
}
