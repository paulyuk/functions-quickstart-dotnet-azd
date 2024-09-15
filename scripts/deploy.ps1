$output = azd env get-values

foreach ($line in $output) {
    if (!($line)){
      break
    }
      $name = $line.Split('=')[0]
      $value = $line.Split('=')[1].Trim('"')
      Set-Item -Path "env:\$name" -Value $value
}

Write-Host "Environment variables set."

$tools = @("az", "func")

foreach ($tool in $tools) {
  if (!(Get-Command $tool -ErrorAction SilentlyContinue)) {
    Write-Host "Error: $tool command line tool is not available, check pre-requisites in README.md"
    exit 1
  }
}

# az account set --subscription $env:AZURE_SUBSCRIPTION_ID
Write-Host $env:AZURE_SUBSCRIPTION_ID

cd ../http

# delete the bin and obj folders to get around an issue - temporary, fix is coming
rm bin -force -recurse
rm obj -force -recurse

# publish the Function app to Azure
func azure functionapp publish $env:AZURE_FUNCTION_NAME --dotnet-isolated

Write-Host "Deployment completed."
cd ../..
