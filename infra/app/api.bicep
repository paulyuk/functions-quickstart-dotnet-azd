param name string
param location string = resourceGroup().location
param tags object = {}
param applicationInsightsName string = ''
param appServicePlanId string
param appSettings object = {}
param runtimeName string 
param runtimeVersion string 
param serviceName string = 'api'
param storageAccountName string
param deploymentStorageContainerName string
param virtualNetworkSubnetId string = ''
param instanceMemoryMB int = 2048
param maximumInstanceCount int = 100
param identityClientId string = ''
@allowed(['SystemAssigned', 'UserAssigned'])
param identityType string
@description('User assigned identity resource id')
param identityId string


var applicationInsightsIdentity = 'ClientId=${identityClientId};Authorization=AAD'

module api '../core/host/functions.bicep' = {
  name: '${serviceName}-functions-module'
  params: {
    name: name
    location: location
    tags: union(tags, { 'azd-service-name': serviceName })
    appSettings: union(appSettings,
      {
        AzureWebJobsStorage__accountName: storageAccountName
        AzureWebJobsStorage__credential : 'managedidentity'
        AzureWebJobsStorage__clientId : identityClientId
        APPLICATIONINSIGHTS_AUTHENTICATION_STRING: applicationInsightsIdentity
      })
    applicationInsightsName: applicationInsightsName
    appServicePlanId: appServicePlanId
    alwaysOn: false
    managedIdentity: true
    identityType: identityType
    identityId: identityId
    kind: 'functionapp,linux'
    runtimeName: runtimeName
    runtimeVersion: runtimeVersion
    storageAccountName: storageAccountName
    virtualNetworkSubnetId: virtualNetworkSubnetId
    scmDoBuildDuringDeployment: false
    enableOryxBuild: false
  }
}

output SERVICE_API_NAME string = api.outputs.name
output SERVICE_API_IDENTITY_PRINCIPAL_ID string = api.outputs.identityPrincipalId
