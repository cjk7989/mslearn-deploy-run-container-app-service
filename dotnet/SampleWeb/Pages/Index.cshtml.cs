using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using Azure;
using Microsoft.Azure.Cosmos;
using Microsoft.AspNetCore.Mvc;
using System.Security.Policy;

namespace SampleWeb.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;

        private readonly string adminStorageAccountConfigurationFilePath = Environment.GetEnvironmentVariable("AdminStorageAccountConfigurationFilePath");
        private readonly string storageContainerName = "testcontainer";
        private readonly string storageBlobName = "storagetest.txt";

        private readonly string cosmosEndpoint = Environment.GetEnvironmentVariable("CosmosConnectionString");
        private string cosmosDatabaseName = "dswadb";
        private string cosmosContainerName = "cosmostest";

        private readonly string userIdentityId = Environment.GetEnvironmentVariable("UserAssignedClientId");

        private BlobServiceClient blobServiceClient;
        private BlobContainerClient blobContainerClient;
        private BlobClient blobClient;

        private CosmosClient cosmosClient;
        private Database cosmosDatabase;
        private Container cosmosContainer;

        // The log to show in the web page
        public string LogMessage { get; set; }

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            string domainName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
            ViewData["Host"] = domainName;
            if (string.IsNullOrEmpty(domainName))
            {
                ViewData["Site"] = "Sample Container";
                ViewData["Host"] = "local or unknown";
            }
            else if (domainName.ToLower().Contains("msha"))
            {
                ViewData["Site"] = "Dedicated MSHA";
            }
            else if (domainName.ToLower().Contains("cds"))
            {
                ViewData["Site"] = "Dedicated CDS";
            }
            else
            {
                ViewData["Site"] = "Sample Container Web";
            }

            string sku = Environment.GetEnvironmentVariable("IsDedicated");

            if (string.IsNullOrEmpty(sku))
            {
                ViewData["Sku"] = "Unknown";
            }
            else
            {
                ViewData["Sku"] = sku;
            }

            string userAssignedIdentityId = Environment.GetEnvironmentVariable("UserAssignedClientId");
            ViewData["UserAssignedClientId"] = userAssignedIdentityId;

            ViewData["AdminStorageAccountConfigurationFilePath"] = adminStorageAccountConfigurationFilePath;
            ViewData["CosmosConnectionString"] = Environment.GetEnvironmentVariable("CosmosConnectionString"); ;          
        }

        public async Task<IActionResult> OnGetStorageAsync()
        {
            Uri uri1 = new Uri(adminStorageAccountConfigurationFilePath);
            string storageAddress = uri1.GetLeftPart(UriPartial.Authority);

            // ' storage blob data contributor' is required before using MSI
            try
            {
                var messageSuffix = " from using default credential.";
                if (!string.IsNullOrEmpty(userIdentityId))
                {
                    blobServiceClient = new BlobServiceClient(
                        new Uri(storageAddress),
                        new ManagedIdentityCredential(userIdentityId)
                    );
                    messageSuffix = $" from using user assigned identity {userIdentityId}.";
                }
                else
                {
                    DefaultAzureCredential defaultAzureCredential = new DefaultAzureCredential();
                    blobServiceClient = new BlobServiceClient(
                        new Uri(storageAddress),
                        defaultAzureCredential
                        );
                }

                // Create blobContainerClient if not exists
                blobContainerClient = blobServiceClient.GetBlobContainerClient(storageContainerName);
                if (!await blobContainerClient.ExistsAsync())
                {
                    blobContainerClient = await blobServiceClient.CreateBlobContainerAsync(storageContainerName);
                }

                // Create BlobClient if not exists
                blobClient = blobContainerClient.GetBlobClient(storageBlobName);
                if (!await blobClient.ExistsAsync())
                {
                    await blobClient.UploadAsync(new MemoryStream());
                }

                // Write log to blob
                string log = $"[{DateTime.Now}] This is a log message from {ViewData["Site"]} and {messageSuffix}.\n";
                await blobClient.UploadAsync(BinaryData.FromString(log), overwrite: true);

                // Read log from blob
                using (var stream = new MemoryStream())
                {
                    await blobClient.DownloadToAsync(stream);
                    LogMessage = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                }
            }
            catch (Exception ex)
            {
                LogMessage = "Sorry, something went wrong. Please try again later.\n";
                LogMessage += ex.Message;
            }
            return Content(LogMessage);
        }

        public async Task<IActionResult> OnGetCosmosAsync()
        {
            try
            {
                var messageSuffix = " from using default credential.";
                string dbname = Environment.GetEnvironmentVariable("CosmosDatabaseName");
                if (!string.IsNullOrEmpty(dbname))
                {
                    cosmosDatabaseName = dbname;
                }

                string containername = Environment.GetEnvironmentVariable("CosmosContainerName");
                if (!string.IsNullOrEmpty(containername))
                {
                    cosmosContainerName = containername;
                }

                if (string.IsNullOrEmpty(userIdentityId))
                {
                    DefaultAzureCredential defaultAzureCredential = new DefaultAzureCredential();
                    cosmosClient = new CosmosClient(
                        cosmosEndpoint,
                        defaultAzureCredential
                    );
                }
                else
                {

                    cosmosClient = new CosmosClient(
                        cosmosEndpoint,
                        new ManagedIdentityCredential(userIdentityId)
                    );
                    messageSuffix = $" from using user assigned identity {userIdentityId}.";
                }

                // Create a database asynchronously if it doesn't already exist
                //cosmosDatabase = await cosmosClient.CreateDatabaseIfNotExistsAsync(
                //    id: cosmosDatabaseName
                //);

                cosmosDatabase = cosmosClient.GetDatabase(cosmosDatabaseName);

                // Create a container asynchronously if it doesn't already exist
                //cosmosContainer = await cosmosDatabase.CreateContainerIfNotExistsAsync(
                //    id: cosmosContainerName,
                //    partitionKeyPath: "/id",
                //    throughput: 400
                //);

                cosmosContainer = cosmosDatabase.GetContainer(cosmosContainerName);

                // Generate a random product Id
                string productId = Guid.NewGuid().ToString();

                // Create an item
                Log tempLog = new Log
                {
                    id = productId,
                    Content =  $"[{DateTime.Now}] This is a log message from {ViewData["Site"]} and {messageSuffix}\n.",
                    Date = DateTime.UtcNow.ToString(),
                };

                // Insert to cosmosContainer
                await cosmosContainer.UpsertItemAsync<Log>(tempLog);

                // Read from cosmosContainer
                Log result = await cosmosContainer.ReadItemAsync<Log>(productId, new PartitionKey(productId));

                LogMessage = $"{result.Content}\n";
            }
            catch (Exception ex)
            {
                LogMessage = "Sorry, something went wrong. Please try again later.\n";
                LogMessage += ex.Message;
            }
            return Content(LogMessage);
        }
    }

    public class Log
    {
        public string id { get; set; }
        public string Content { get; set; }
        public string Date { get; set; }
    }
}
