#!/bin/bash

set -o allexport; source ./.azure/ep10/.env; set +o allexport

echo "Environment variables set."
echo ""
# echo "Showing environment variables with env command:"
# env

commands=("az" "func" "zip" "dotnet")

for cmd in "${commands[@]}"; do
  if ! command -v "$cmd" &>/dev/null; then
    echo "Error: $cmd command is not available, check pre-requisites in README.md"
    exit 1
  fi
done

cd ./http

# Define variables
functionAppPath="."
zipFilePath="./http/bin/publish"
zipFileName="functions.zip"

# delete the bin and obj folders to get around an issue - temporary, fix is coming
rm -rf bin
rm -rf obj

# Build the function app project
# dotnet build

# Create the the application zip package
azd package --output-path $zipFilePath/$zipFileName

# Upload the zip package to Azure Storage Blob container
echo "Uploading functions.zip to Azure Storage Blob container $storageAccountName/$storageContainerName/$blobName..."
echo "az storage blob upload --account-name $AZURE_STORAGE_CONTAINER_NAME --container-name $AZURE_STORAGE_CONTAINER_NAME --name $zipFileName --file $zipFilePath/$zipFileName --auth-mode login --overwrite"
az storage blob upload --account-name $AZURE_STORAGE_CONTAINER_NAME --container-name $AZURE_STORAGE_CONTAINER_NAME --name $zipFileName --file $zipFilePath/$zipFileName --auth-mode login --overwrite

echo "Deployed functions.zip successfully to $storageAccountName/$storageContainerName/$blobName"

# Restarting the function app with new functions.zip blob
echo "Restarting the function app..."
echo "az functionapp restart --name $AZURE_FUNCTION_NAME --resource-group $resourceGroupName"
az functionapp restart --name $AZURE_FUNCTION_NAME --resource-group $resourceGroupName

# Purge a deleted resource
# az resource delete --ids /subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$AZURE_RESOURCE_GROUP/providers/Microsoft.CognitiveServices/accounts/$AZURE_FORMRECOGNIZER_SERVICE
# az resource delete --ids /subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$AZURE_RESOURCE_GROUP/providers/Microsoft.CognitiveServices/accounts/$AZURE_SEARCH_SERVICE

# recover
# az cognitiveservices account recover --location $AZURE_LOCATION --name $AZURE_FORMRECOGNIZER_SERVICE --resource-group $AZURE_RESOURCE_GROUP
