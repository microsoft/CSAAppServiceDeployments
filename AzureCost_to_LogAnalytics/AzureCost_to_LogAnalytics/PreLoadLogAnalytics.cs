using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using System.Linq;
using System.Data;
using Microsoft.Azure.Services.AppAuthentication;

namespace AzureCost_to_LogAnalytics
{
    public static class PreLoadLogAnalytics
    {
        private static string[] scopes = (Environment.GetEnvironmentVariable("scope")).Split(',');
        private static string workspaceid = Environment.GetEnvironmentVariable("workspaceid");
        private static string workspacekey = Environment.GetEnvironmentVariable("workspacekey");
        private static string logName = Environment.GetEnvironmentVariable("logName");
        
        public static string jsonResult { get; set; }

        public static async void callAPIPage(string scope, string skipToken, string workspaceid, string workspacekey, string logName, ILogger log, string myJson)
        {
            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            string AuthToken = await azureServiceTokenProvider.GetAccessTokenAsync("https://management.azure.com/");

            using (var client = new HttpClient())
            {
                // Setting Authorization.  
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);


                // Setting Base address.  
                client.BaseAddress = new Uri("https://management.azure.com");
                // Setting content type.  
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // Initialization.  
                HttpResponseMessage response = new HttpResponseMessage();

                AzureLogAnalytics logAnalytics = new AzureLogAnalytics(
                    workspaceId: $"{workspaceid}",
                    sharedKey: $"{workspacekey}",
                    logType: $"{logName}");

                string newURL = "/" + scope + "/providers/Microsoft.CostManagement/query?api-version=2019-11-01&" + skipToken;
                response = await client.PostAsync(newURL, new StringContent(myJson, Encoding.UTF8, "application/json"));
                QueryResults result = JsonConvert.DeserializeObject<QueryResults>(response.Content.ReadAsStringAsync().Result);

                
                jsonResult = "[";
                for (int i = 0;  i < result.properties.rows.Length; i++)
                {
                    object[] row = result.properties.rows[i];
                    double cost = Convert.ToDouble(row[0]);

                    if (i == 0)
                    {
                        jsonResult += $"{{\"PreTaxCost\": {cost},\"Date\": \"{row[1]}\",\"ResourceId\": \"{row[2]}\",\"ResourceType\": \"{row[3]}\",\"SubscriptionName\": \"{row[4]}\",\"ResourceGroup\": \"{row[5]}\"}}";
                    }
                    else
                    {
                        jsonResult += $",{{\"PreTaxCost\": {cost},\"Date\": \"{row[1]}\",\"ResourceId\": \"{row[2]}\",\"ResourceType\": \"{row[3]}\",\"SubscriptionName\": \"{row[4]}\",\"ResourceGroup\": \"{row[5]}\"}}";
                    }
                }

                jsonResult += "]";
                
                //log.LogInformation($"Cost Data: {jsonResult}");
                logAnalytics.Post(jsonResult);

                string nextLink = null;
                nextLink = result.properties.nextLink.ToString();
                
                if (!string.IsNullOrEmpty(nextLink))
                {
                    skipToken = nextLink.Split('&')[1];
                    Console.WriteLine(skipToken);
                    callAPIPage(scope, skipToken, workspaceid, workspacekey, logName, log, myJson);
                }
            }

        }
    
    
        [FunctionName("PreLoadLogAnalytics")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var azureServiceTokenProvider = new AzureServiceTokenProvider();
            string AuthToken = await azureServiceTokenProvider.GetAccessTokenAsync("https://management.azure.com/");
            Console.WriteLine(AuthToken);

            using (var client = new HttpClient())
            {
                // Setting Authorization.  
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

                // Setting Base address.  
                client.BaseAddress = new Uri("https://management.azure.com");

                // Setting content type.  
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // Initialization.  
               HttpResponseMessage response = new HttpResponseMessage();

               DateTime startTime = DateTime.Now.AddDays(-180);
               DateTime endTime = DateTime.Now;
               string start = startTime.ToString("MM/dd/yyyy");
               string end = endTime.ToString("MM/dd/yyyy");

                string myJson = @"{
                        'dataset': {
                            'aggregation': {
                            'totalCost': {
                                'function': 'Sum',
                                'name': 'PreTaxCost'
                            }
                        },
                        'granularity': 'Daily',
                        'grouping': [
                            {
                                'name': 'ResourceId',
                                'type': 'Dimension'
                            },
                            {
                                'name': 'ResourceType',
                                'type': 'dimension'
                            },
                            {
                                'name': 'SubscriptionName',
                                'type': 'dimension'
                            },
                            {
                                'name': 'ResourceGroup',
                                'type': 'dimension'
                            }
                        ]
                    },
                    'timePeriod': {
                        'from': '" + start + @"',
                        'to': '" + end + @"'
                    },
                    'timeframe': 'Custom',
                    'type': 'Usage'
                }";

                    log.LogInformation($"Cost Query: {myJson}");

                    AzureLogAnalytics logAnalytics = new AzureLogAnalytics(
                        workspaceId: $"{workspaceid}",
                        sharedKey: $"{workspacekey}",
                        logType: $"{logName}");

                    foreach (string scope in scopes)
                    {
                        log.LogInformation($"Scope: {scope}");
                        // HTTP Post
                        response = await client.PostAsync("/" + scope + "/providers/Microsoft.CostManagement/query?api-version=2019-11-01", new StringContent(myJson, Encoding.UTF8, "application/json"));

                        Console.WriteLine(client);
                        QueryResults result = Newtonsoft.Json.JsonConvert.DeserializeObject<QueryResults>(response.Content.ReadAsStringAsync().Result);
                        
                        
                        jsonResult = "[";
                        for (int i = 0; i < result.properties.rows.Length; i++)
                        {
                            object[] row = result.properties.rows[i];
                            double cost = Convert.ToDouble(row[0]);

                            if (i == 0)
                            {
                                jsonResult += $"{{\"PreTaxCost\": {cost},\"Date\": \"{row[1]}\",\"ResourceId\": \"{row[2]}\",\"ResourceType\": \"{row[3]}\",\"SubscriptionName\": \"{row[4]}\",\"ResourceGroup\": \"{row[5]}\"}}";
                            }
                            else
                            {
                                jsonResult += $",{{\"PreTaxCost\": {cost},\"Date\": \"{row[1]}\",\"ResourceId\": \"{row[2]}\",\"ResourceType\": \"{row[3]}\",\"SubscriptionName\": \"{row[4]}\",\"ResourceGroup\": \"{row[5]}\"}}";
                            }
                        }

                        jsonResult += "]";

                        log.LogInformation($"Cost Data: {jsonResult}");
                        logAnalytics.Post(jsonResult);

                    string nextLink = result.properties.nextLink.ToString();

                    if (!string.IsNullOrEmpty(nextLink))
                    {
                        string skipToken = nextLink.Split('&')[1];
                        callAPIPage(scope, skipToken, workspaceid, workspacekey, logName, log, myJson);
                    }

                    //return new OkObjectResult(jsonResult);
                }

            }
                
            return new OkObjectResult(jsonResult);
        }
        
    }
}
