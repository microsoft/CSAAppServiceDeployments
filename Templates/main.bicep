targetScope = 'resourceGroup'

@description('Resource names prefix, it will be used in storage account name, only letters and digits allowed.')
@maxLength(6)
param deploymentPrefix string = 'acad'

@description('Azure region for App Insights, defaults to is resource group location.')
param appInsightsLocation string = resourceGroup().location

@description('One or many scopes for the cost api query separated by a comma (,). ex: subscriptions/5f1c1322-cebc-4ea3-8779-fac7d666e18f. Reference here: https://docs.microsoft.com/en-us/rest/api/cost-management/query/usage')
param scopes string

@description('Function app package repository URI.')
param packageUri string = 'https://github.com/slapointe/azure-cost-anomalydetection.git'

@description('Function app package repository branch.')
param packageBranch string = 'main'

@description('Display name for the cost anomaly workbook.')
param workbookDisplayName string = 'Azure Cost Anomaly detection'

var uniqueHash = uniqueString(resourceGroup().id)
var laName = '${deploymentPrefix}-la-${uniqueHash}'
var appName = '${deploymentPrefix}-costingestionfunc-${uniqueHash}'
var appInsightsName = '${deploymentPrefix}-ai-${uniqueHash}'
var saName = toLower('${deploymentPrefix}0sa0${uniqueHash}')
var wbName = '${deploymentPrefix}-wb-${uniqueHash}'
var saConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${funcStorage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${listKeys(funcStorage.id, funcStorage.apiVersion).keys[0].value}'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2021-06-01' = {
  name: laName
  location: resourceGroup().location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: appInsightsLocation
  kind: 'web'
  properties:{
      Application_Type: 'web'
  }  
}

resource funcStorage 'Microsoft.Storage/storageAccounts@2021-04-01' = {
  name: saName
  location: resourceGroup().location  
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'Storage'  
}

resource appServicePlan 'Microsoft.Web/serverfarms@2021-01-15' = {
  name: appName
  location: resourceGroup().location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
}

resource funcApp 'Microsoft.Web/sites@2021-01-15' = {
  name: appName
  location: resourceGroup().location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id    
    siteConfig: {
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: saConnectionString
        }
        {
          name: 'AzureWebJobsDashboard'
          value: saConnectionString
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: saConnectionString
        }  
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: toLower(appName)
        }  
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~3'
        }             
        {
          name: 'WEBSITE_NODE_DEFAULT_VERSION'
          value: '~10'
        }             
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet'
        }             
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '0'
        }             
        {
          name: 'logName'
          value: 'AzureCostAnamolies'
        }
        {
          name: 'scope'
          value: scopes
        }
        {
          name: 'workspaceid'
          value: logAnalytics.properties.customerId
        }
        {
          name: 'workspacekey'
          value: listKeys(logAnalytics.id, logAnalytics.apiVersion).primarySharedKey
        } 
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsights.properties.InstrumentationKey
        }
      ]      
    }
  }
  resource sourceControl 'sourcecontrols@2021-01-15' = {
    name: 'web'
    properties: {
      repoUrl: packageUri
      branch: packageBranch
      isManualIntegration: true
    }
  }
}

resource workbook 'Microsoft.Insights/workbooks@2021-03-08' = {
  name: guid(wbName)
  location: resourceGroup().location
  identity: {
    type: 'None'
  }
  kind: 'shared'
  properties: {
    displayName: workbookDisplayName
    category: 'workbook'
    version: 'Notebook/1.0'
    serializedData: replace(loadTextContent('./CostWorkbook.json'), '{{workspaceResourceId}}', logAnalytics.id)
    sourceId: 'azure monitor'
  }
}

output functionAppName string = funcApp.name
