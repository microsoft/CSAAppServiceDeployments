using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Services.AppAuthentication;
using System.Threading.Tasks;

namespace AzureCost_to_LogAnalytics
{
  public static class AzureCost_to_LogAnalytics
  {//Environment.GetEnvironmentVariable("ClientId")
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

        //log.LogInformation($"Cost Data: {jsonResult}");
        logAnalytics.Post(jsonResult);

        if (result.properties.nextLink != null)
        {
          string nextLink = result.properties.nextLink.ToString();
          skipToken = nextLink.Split('&')[1];
          Console.WriteLine(skipToken);
          callAPIPage(scope, skipToken, workspaceid, workspacekey, logName, log, myJson);
        }
      }

    }


    [FunctionName("DailyCostLoad")]
    public static async Task Run([TimerTrigger("0 0 1 * * *")] TimerInfo myTimer, ILogger log)
    {
      DateTime start = DateTime.Now.AddDays(-1);

      string time = start.ToString("MM/dd/yyyy");


      log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

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
                    'from': '" + time + @"',
                    'to': '" + time + @"'
                },
                'timeframe': 'Custom',
                'type': 'Usage'
            }";
        Console.WriteLine(myJson);
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
            object[] row;
            try
            {
              row = result.properties.rows[i];
              double cost = Convert.ToDouble(row[0]);
              string sDate = Convert.ToString(row[1]);
              string sResourceId;
              try
              {
                sResourceId = Convert.ToString(row[2]);
              }
              catch
              {
                sResourceId = "";
              }
              string sResourceType;
              try
              {
                sResourceType = Convert.ToString(row[3]);
              }
              catch
              {
                sResourceType = "";
              }
              string sSubscriptionName;
              try
              {
                sSubscriptionName = Convert.ToString(row[4]);
              }
              catch
              {
                sSubscriptionName = "";
              }
              string sResourceGroup;
              try
              {
                sResourceGroup = Convert.ToString(row[5]);
              }
              catch
              {
                sResourceGroup = "";
              }


              if (i == 0)
              {
                jsonResult += $"{{\"PreTaxCost\": {cost},\"Date\": \"{sDate}\",\"ResourceId\": \"{sResourceId}\",\"ResourceType\": \"{sResourceType}\",\"SubscriptionName\": \"{sSubscriptionName}\",\"ResourceGroup\": \"{sResourceGroup}\"}}";
              }
              else
              {
                jsonResult += $",{{\"PreTaxCost\": {cost},\"Date\": \"{sDate}\",\"ResourceId\": \"{sResourceId}\",\"ResourceType\": \"{sResourceType}\",\"SubscriptionName\": \"{sSubscriptionName}\",\"ResourceGroup\": \"{sResourceGroup}\"}}";
              }
            }
            catch
            { }


          }
          jsonResult += "]";

          log.LogInformation($"Cost Data: {jsonResult}");
          Console.WriteLine($"Cost Data: {jsonResult}");
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
    }
  }
}
