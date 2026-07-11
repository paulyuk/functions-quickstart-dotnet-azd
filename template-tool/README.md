# Template Tool POC

Turn an existing runnable AZD template folder into a customized app without writing or maintaining `template.json`.

This first prototype targets the .NET 10 Azure Functions quickstart in this repo.

## What it does

Given a source folder, the tool can:

- find the .NET project name
- find the project folder and `.csproj`
- find the `.sln`
- find the C# namespace
- find Azure Functions names
- find the `azure.yaml` app name
- copy the source to a new output folder
- rename the project folder and `.csproj`
- update the solution file
- update C# namespaces
- update `azure.yaml`
- update the launch profile when it matches the old project identity
- run `dotnet build` on the generated result

## Build the tool

From the repo root:

```pwsh
dotnet build .\template-tool\TemplatePoc.csproj
```

## Analyze an existing template folder

Run this against any existing folder that contains the AZD template or project.

```pwsh
dotnet run --project .\template-tool\TemplatePoc.csproj -- analyze --source .
```

Example output:

```json
{
  "ProjectName": "http",
  "ProjectFolder": "http",
  "ProjectFile": "http\\http.csproj",
  "SolutionFile": "http.sln",
  "Namespace": "Company.Function",
  "AzdName": "starter-dotnet8-flex-func",
  "Functions": [
    "httpget",
    "httppost"
  ]
}
```

## Preview what will change

Use `plan` before writing anything.

```pwsh
dotnet run --project .\template-tool\TemplatePoc.csproj -- plan `
  --source . `
  --name MyFunctionApp `
  --namespace Contoso.MyFunctionApp
```

The tool prints the detected values and the files it would change.

## Generate the customized result

Use `apply` to copy the source folder, customize the copy, and build it.

```pwsh
dotnet run --project .\template-tool\TemplatePoc.csproj -- apply `
  --source . `
  --output .\out\MyFunctionApp `
  --name MyFunctionApp `
  --namespace Contoso.MyFunctionApp
```

When this succeeds, the generated app is here:

```pwsh
.\out\MyFunctionApp
```

You can open it, build it, or run AZD from that folder:

```pwsh
cd .\out\MyFunctionApp
dotnet build
azd up
```

## Optional AZD name

By default, `MyFunctionApp` becomes this AZD app name:

```text
my-function-app
```

Pass `--azd-name` if you want a different value in `azure.yaml`.

```pwsh
dotnet run --project .\template-tool\TemplatePoc.csproj -- apply `
  --source . `
  --output .\out\MyFunctionApp `
  --name MyFunctionApp `
  --namespace Contoso.MyFunctionApp `
  --azd-name contoso-functions-demo
```

## Current limits

This is intentionally small.

- .NET 10 Azure Functions only for now
- no large manifest
- no `template.json`
- no README tokenization
- no `infra` tokenization unless a later phase adds explicit hints

The point of the POC is to prove the common path can be convention based and stay easy to explain.
