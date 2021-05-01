using NLog;
using PuppeteerSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StoreCheckoutBot.SiteCrawlers
{
    public abstract class PageCrawlerBase
    {
        protected string _username { get; set; }
        protected string _password { get; set; }
        protected string _pageUrl { get; set; }
        protected string _screenshotLocation { get; set; }
        protected int _screenshotWidth { get; set; }
        protected int _screenshotHeight { get; set; }
        protected decimal _maxPrice { get; set; }
        protected int _refreshIntervalSeconds { get; set; }
        protected Logger _logger { get; set; }
        protected Browser _browser { get; set; }

        public PageCrawlerBase(string pageUrl, string username, string password, string screenshotLocation, int screenshotWidth, int screenshotHeight, decimal maxPrice, int refreshIntervalSeconds, Browser browser, Logger logger) {
            _pageUrl = pageUrl;
            _username = username;
            _password = password;
            _screenshotLocation = screenshotLocation;
            _screenshotWidth = screenshotWidth;
            _screenshotHeight = screenshotHeight;
            _maxPrice = maxPrice;
            _refreshIntervalSeconds = refreshIntervalSeconds;
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
