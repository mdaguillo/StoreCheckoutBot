using NLog;
using PuppeteerSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StoreCheckoutBot.SiteCrawlers
{
    public abstract class PageCrawlerBase
    {
        protected BotSettings _botSettings { get; set; }
        protected StoreDetails _storeDetails { get; set; }
        protected ProductDetails _productDetails { get; set; }
        protected ProductPage _productPage { get; set; }
        protected Logger _logger { get; set; }
        protected Browser _browser { get; set; }

        public PageCrawlerBase(BotSettings botSettings, StoreDetails storeDetails, ProductDetails productDetails, ProductPage productPage, Browser browser, Logger logger) {
            _botSettings = botSettings;
            _storeDetails = storeDetails;
            _productDetails = productDetails;
            _productPage = productPage;
            _logger = logger;
            _browser = browser;
        }

        public async virtual Task CrawlPageAsync(CancellationToken token) {
            throw new NotImplementedException();
        }
        public async virtual Task LoginAsync() {
            throw new NotImplementedException();
        }
    }
}
