using System.Collections.Generic;

namespace StoreCheckoutBot
{
    /// <summary>
    /// Strongly typed object mapping to the appsettings.json configuration file
    /// </summary>
    public class Configuration
    {
        public BotSettings BotSettings { get; set; } = new BotSettings();

        /// <summary>
        /// The list of stores to crawl
        /// </summary>
        public List<StoreConfig> StoreConfigs { get; set; } = new List<StoreConfig>();
    }

    public class BotSettings 
    {
        /// <summary>
        /// Determines whether Chromium is launched in headless mode. Headless is generally faster
        /// </summary>
        public bool UseHeadlessBrowser { get; set; } = true;

        /// <summary>
        /// The default screenshot width when taking screenshots during the crawling process 
        /// </summary>
        public int ScreenshotWidth { get; set; } = 1920;

        /// <summary>
        /// The default screenshot height when taking screenshots during the crawling process
        /// </summary>
        public int ScreenshotHeight { get; set; } = 1080;

        /// <summary>
        /// The filepath to the screenshot folder e.g. "C:\Screenshots"
        /// </summary>
        public string ScreenshotFolderLocation { get; set; } = ".\\CheckoutBotScreenshots";

        public string DiscordBotToken { get; set; }

        public string DiscordBotServerName { get; set; }

        public string UserDiscordName { get; set; }
    }

    /// <summary>
    /// A grouping of settings attributed to a single store. For example: Amazon.
    /// </summary>
    public class StoreConfig
    { 
        public StoreDetails StoreDetails { get; set; }

        /// <summary>
        /// A collection of products to crawl on this site
        /// </summary>
        public List<Product> Products { get; set; } = new List<Product>();
    }

    public class StoreDetails
    {
        /// <summary>
        /// The name of the store. Also used to map to the login function, and site specific crawler
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Username to login to the site
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Password to login to the site
        /// </summary>
        public string Password { get; set; }
    }

    public class Product
    {
        public ProductDetails ProductDetails { get; set; }
        
        /// <summary>
        /// The collection of pages for this product
        /// </summary>
        public List<ProductPage> ProductPages { get; set; }  
    }

    public class ProductDetails 
    {
        /// <summary>
        /// The max price you're willing to spend on this item
        /// </summary>
        public decimal MaxPrice { get; set; }
    }

    /// <summary>
    /// Grouping of settings that give details about a product page
    /// </summary>
    public class ProductPage
    { 
        /// <summary>
        /// The url of the product page
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// How frequently (in seconds) the page should refresh to check if the item is in stock
        /// </summary>
        public int RefreshIntervalSeconds { get; set; } = 10;
    }
}
