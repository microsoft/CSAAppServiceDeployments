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
        private static string ClientId = Environment.GetEnvironmentVariable("ClientId");
        private static string ServicePrincipalPassword = Environment.GetEnvironmentVariable("ServicePrincipalPassword");
        private static string AzureTenantId = Environment.GetEnvironmentVariable("AzureTenantId");
        private static string[] scopes = (Environment.GetEnvironmentVariable("scope")).Split(',');
        private static string workspaceid = Environment.GetEnvironmentVariable("workspaceid");
        private static string workspacekey = Environment.GetEnvironmentVariable("workspacekey");
        private static string logName = Environment.GetEnvironmentVariable("logName");
        private static double daystoload = Convert.ToDouble(Environment.GetEnvironmentVariable("daystoLoad"));

        public static string jsonResult { get; set; }

        private static string AuthToken { get; set; }
        private static TokenCredentials TokenCredentials { get; set; }

        private static string GetAuthorizationToken()
        {
            ClientCredential cc = new ClientCredential(ClientId, ServicePrincipalPassword);
            var context = new AuthenticationContext("https://login.windows.net/" + AzureTenantId);
            var result = context.AcquireTokenAsync("https://management.azure.com/", cc);
            if (result == null)
            {
                throw new InvalidOperationException("Failed to obtain the JWT token");
            }

            return result.Result.AccessToken;
        }


        [FunctionName("PreLoadLogAnalytics")]
        public static async void Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            DateTime time = DateTime.Now.AddDays(-1);

            string start = time.ToString("MM/dd/yyyy");
            string end = time.AddDays(-daystoload).ToString("MM/dd/yyyy");

            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            //AuthToken = GetAuthorizationToken();
            //TokenCredentials = new TokenCredentials(AuthToken);

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
                            'name': 'Meter',
                            'type': 'dimension'
                        },
                        {
                            'name': 'MeterCategory',
                            'type': 'dimension'
                        },
                        {
                            'name': 'MeterSubcategory',
                            'type': 'dimension'
                        },
                        {
                            'name': 'SubscriptionName',
                            'type': 'dimension'
                        },
                        {
                            'name': 'ServiceName',
                            'type': 'dimension'
                        },
                        {
                            'name': 'ServiceTier',
                            'type': 'dimension'
                        }
                    ]
                },
                'timePeriod': {
                    'from': '" + end + @"',
                    'to': '" + start + @"'
                },
                'timeframe': 'Custom',
                'type': 'Usage'
            }";

                AzureLogAnalytics logAnalytics = new AzureLogAnalytics(
                    workspaceId: $"{workspaceid}",
                    sharedKey: $"{workspacekey}",
                    logType: $"{logName}");

                foreach (string scope in scopes)
                {
                    Console.WriteLine(scope);
                    // HTTP Post
                    response = await client.PostAsync("/" + scope + "/providers/Microsoft.CostManagement/query?api-version=2019-11-01", new StringContent(myJson, Encoding.UTF8, "application/json"));

                    QueryResults result = Newtonsoft.Json.JsonConvert.DeserializeObject<QueryResults>(response.Content.ReadAsStringAsync().Result);


                    jsonResult = "[";
                    for (int i = 0; i < result.properties.rows.Length; i++)
                    {
                        object[] row = result.properties.rows[i];
                        double cost = Convert.ToDouble(row[0]);

                        if (i == 0)
                        {
                            jsonResult += $"{{\"PreTaxCost\": {cost},\"Date\": \"{row[1]}\",\"ResourceId\": \"{row[2]}\",\"ResourceType\": \"{row[3]}\",\"Meter\": \"{row[4]}\",\"MeterCategory\": \"{row[5]}\",\"MeterSubcategory\": \"{row[6]}\",\"SubscriptionName\": \"{row[7]}\",\"ServiceName\": \"{row[8]}\",\"ServiceTier\": \"{row[9]}\"}}";
                        }
                        else
                        {
                            jsonResult += $",{{\"PreTaxCost\": {cost},\"Date\": \"{row[1]}\",\"ResourceId\": \"{row[2]}\",\"ResourceType\": \"{row[3]}\",\"Meter\": \"{row[4]}\",\"MeterCategory\": \"{row[5]}\",\"MeterSubcategory\": \"{row[6]}\",\"SubscriptionName\": \"{row[7]}\",\"ServiceName\": \"{row[8]}\",\"ServiceTier\": \"{row[9]}\"}}";
                        }
                    }

                    jsonResult += "]";
                    logAnalytics.Post(jsonResult);
                }
            }
        }
    }
}
