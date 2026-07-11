# Template Tool POC

Create a customized app from a runnable AZD template without writing or maintaining `template.json`.

This first prototype targets the .NET 10 Azure Functions quickstart.

## Quickstart

From this repo root, publish the standalone tool.

Windows:

```pwsh
dotnet publish .\template-tool\TemplatePoc.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false `
  --output .\template-tool\dist
```

macOS or Linux:

```bash
dotnet publish ./template-tool/TemplatePoc.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false \
  --output ./template-tool/dist
```

Use `osx-arm64`, `osx-x64`, `linux-arm64`, or `linux-x64` for your machine.

Then create an app from the short AZD template name:

Windows:

```pwsh
.\template-tool\dist\template-tool.exe apply `
  --source functions-quickstart-dotnet-azd `
  --output .\out\MyFunctionApp `
  --name MyFunctionApp `
  --namespace Contoso.MyFunctionApp
```

macOS or Linux:

```bash
./template-tool/dist/template-tool apply \
  --source functions-quickstart-dotnet-azd \
  --output ./out/MyFunctionApp \
  --name MyFunctionApp \
  --namespace Contoso.MyFunctionApp
```

Open the generated app:

Windows:

```pwsh
cd .\out\MyFunctionApp
dotnet build
azd up
```

macOS or Linux:

```bash
cd ./out/MyFunctionApp
dotnet build
azd up
```

## Most common scenarios

### 1. Create from an AZD template name

Use this when you know the Azure Samples template name.

```pwsh
.\template-tool\dist\template-tool.exe apply `
  --source functions-quickstart-dotnet-azd `
  --output .\out\MyFunctionApp `
  --name MyFunctionApp `
  --namespace Contoso.MyFunctionApp
```

On macOS or Linux, use `./template-tool/dist/template-tool` and forward slash paths.

The tool treats this source:

```text
functions-quickstart-dotnet-azd
```

as this repo:

```text
https://github.com/Azure-Samples/functions-quickstart-dotnet-azd.git
```

### 2. Create from a GitHub repo URL

Use this when the template is in a repo you can clone.

```pwsh
.\template-tool\dist\template-tool.exe apply `
  --source https://github.com/Azure-Samples/functions-quickstart-dotnet-azd.git `
  --output .\out\MyFunctionApp `
  --name MyFunctionApp `
  --namespace Contoso.MyFunctionApp
```

### 3. Create from a local folder

Use this when you already cloned or edited the template locally.

```pwsh
.\template-tool\dist\template-tool.exe apply `
  --source C:\src\functions-quickstart-dotnet-azd `
  --output .\out\MyFunctionApp `
  --name MyFunctionApp `
  --namespace Contoso.MyFunctionApp
```

## Source formats

`--source` can be any of these:

```pwsh
--source .
--source C:\src\functions-quickstart-dotnet-azd
--source ./functions-quickstart-dotnet-azd
--source https://github.com/Azure-Samples/functions-quickstart-dotnet-azd.git
--source Azure-Samples/functions-quickstart-dotnet-azd
--source functions-quickstart-dotnet-azd
```

GitHub sources are cloned to a temporary folder before the tool reads them.

## Build from source instead

If you do not need a standalone exe, you can run directly from source:

```pwsh
dotnet run --project .\template-tool\TemplatePoc.csproj -- analyze --source .
```

## Other modes

### Analyze

Use `analyze` to see what the tool detects without planning or writing changes.

```pwsh
.\template-tool\dist\template-tool.exe analyze `
  --source functions-quickstart-dotnet-azd
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

### Plan

Use `plan` to preview the files and values that would change.

```pwsh
.\template-tool\dist\template-tool.exe plan `
  --source functions-quickstart-dotnet-azd `
  --name MyFunctionApp `
  --namespace Contoso.MyFunctionApp
```

### Apply

Use `apply` to copy the source, customize the copy, and run `dotnet build`.

```pwsh
.\template-tool\dist\template-tool.exe apply `
  --source functions-quickstart-dotnet-azd `
  --output .\out\MyFunctionApp `
  --name MyFunctionApp `
  --namespace Contoso.MyFunctionApp
```

## Options

| Option | Required | Description |
| --- | --- | --- |
| `--source` | No | Template source. Defaults to the current folder. |
| `--output` | Yes for `apply` | Generated app folder. Must not already exist. |
| `--name` | Yes for `plan` and `apply` | New project and app name. |
| `--namespace` | Yes for `plan` and `apply` | New C# namespace. |
| `--azd-name` | No | Value for `azure.yaml` `name`. Defaults to kebab-case from `--name`. |

## Advanced

Read [HOW_IT_WORKS.md](./HOW_IT_WORKS.md) for detection rules, transformations, current limits, and why the POC avoids `template.json`.
