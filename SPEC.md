# Convention Based Template POC

## Goal

Prove that a runnable AZD sample can serve as the template source of truth without maintaining a large `template.json` file.

The POC should support the common .NET Azure Functions customization cases with conventions first, hints only where needed, and no required template manifest.

## Problem

The current `template.json` approach turns a simple cloneable repo into a large generated manifest. In this fork, `template.json` is 1080 lines, has no `symbols`, no `sources`, and mostly lists explicit read and write actions with `replaceTokens` set to `false`.

That means the maintained value is much smaller than the manifest suggests. The useful work is closer to a few project identity replacements:

- project name
- folder and file names
- namespace
- AZD app name
- function names when the user asks for them

The rest is boilerplate that the template author should not have to maintain.

## Proposal

Create a small convention based template tool that starts from a normal runnable repo and applies targeted transformations.

The long term target is Azure Functions templates across the supported programming languages. The prototype starts with .NET and .NET 10 because this repo already provides a concrete baseline.

The repo remains valid before templating:

```pwsh
git clone <repo>
cd <repo>
azd up
```

The tool then applies common customization:

```pwsh
template-poc new `
  --source . `
  --output .\out\MyFunctionApp `
  --name MyFunctionApp `
  --namespace Contoso.MyFunctionApp
```

No `template.json` is required for the happy path.

## Design Principles

1. Keep the sample repo runnable.
2. Prefer conventions over manifests.
3. Use hints only for values that conventions cannot identify safely.
4. Inspect a small, predictable set of files.
5. Never tokenize documentation by default.
6. Treat `infra` as already parameterized unless a value is explicitly hinted.
7. Show the user a plan before applying changes.
8. Make the result buildable with normal .NET and AZD commands.

## Scope

### Phase 1: .NET 10 Azure Functions

Use this fork as the prototype baseline.

Support:

- .NET isolated worker
- .NET 10
- C# source files
- `.sln`
- `.csproj`
- `Program.cs`
- Azure Functions trigger files
- `azure.yaml`
- existing AZD `infra` parameterization

### Later phases: Azure Functions languages

Extend the same convention based approach across the Azure Functions programming languages.

TODO:

- JavaScript
- TypeScript
- Python
- Java
- Go
- PowerShell
- Custom handlers
- Other supported Azure Functions language stacks that need template coverage

Each language should get its own small convention pack instead of adding a large shared manifest. For example, JavaScript and TypeScript conventions would inspect `package.json`, source entry points, function folders, and host configuration. Python conventions would inspect `requirements.txt`, function folders, and module names. Java conventions would inspect Maven or Gradle project files, package names, and function classes.

### Later phases: other VS Code template engine template types

This POC starts with Azure Functions new app templates. It should not be limited to Functions forever.

TODO:

- Other VS Code template engine template types
- Existing `func.exe` template scenarios beyond Functions quickstarts
- Multi-project templates
- Templates that create only one file
- Templates that add a function to an existing app
- Templates that add configuration or deployment assets to an existing project

The same rule should apply: use conventions and local hints first, and only introduce structured metadata when the template cannot be represented safely any other way.

## File Selection Rules

### Always inspect

- `*.sln`
- `*.csproj`
- `Program.cs`
- Azure Functions `.cs` files
- `azure.yaml`
- `Properties\launchSettings.json`

### Conditionally inspect

- Other `.cs` files if they contain the detected namespace, project name, or function app identity.
- Well known project files that contain obvious project identity values.

### Skip by default

- `README.md`
- `CHANGELOG.md`
- `LICENSE.md`
- `.github\**`
- `.devcontainer\**`
- `infra\**`, except for explicit hints
- generated output folders such as `bin\` and `obj\`

## Hint Format

Hints are comments placed next to source values. They are optional and only used when conventions are ambiguous.

Examples:

```csharp
namespace Company.Function // template: namespace
```

```csharp
[Function("httpget")] // template: function-name
```

```yaml
name: starter-dotnet8-flex-func # template: azd-name
```

Hints should be tiny and local. They should not require a separate registry.

## Detected Tokens

The analyzer should emit a small diagnostic model:

```json
{
  "projectName": "http",
  "projectFile": "http\\http.csproj",
  "solutionFile": "http.sln",
  "namespace": "Company.Function",
  "azdName": "starter-dotnet8-flex-func",
  "functions": [
    "httpget",
    "httppost"
  ]
}
```

This output is for review and debugging. It is not intended to become a maintained template file.

## Transformations

For the first .NET 10 POC, support:

1. Rename the project folder from `http` to the requested project name.
2. Rename `http.csproj` to the requested project name.
3. Update `http.sln` project references.
4. Update namespaces from `Company.Function` to the requested namespace.
5. Update `azure.yaml` `name`.
6. Update launch profile identity if it matches the old project identity.
7. Preserve AZD infrastructure parameterization in `infra\main.parameters.json`.

## CLI Shape

```pwsh
template-poc analyze --source .
template-poc plan --source . --name MyFunctionApp --namespace Contoso.MyFunctionApp
template-poc apply --source . --output .\out\MyFunctionApp --name MyFunctionApp --namespace Contoso.MyFunctionApp
```

`analyze` reports detected values.

`plan` reports files and replacements without writing.

`apply` copies the repo, applies the plan, and validates the result.

## Validation

After `apply`, run:

```pwsh
dotnet restore
dotnet build
```

If AZD is available, also run the lightest available AZD validation that does not require cloud deployment.

## Success Criteria

The POC succeeds if:

1. The source repo remains directly cloneable and runnable.
2. The generated app builds.
3. The common .NET Azure Functions identity values are customized.
4. The maintained template metadata is zero lines for the happy path.
5. Any required hints are local comments, not a separate manifest.
6. The comparison against `template.json` is easy to explain.

## First Implementation Slice

Build a small .NET 10 console tool under `tools\TemplatePoc`.

The first slice should implement:

1. `analyze`
2. `plan`
3. `apply`
4. file copy with default excludes
5. namespace and project rename
6. solution and project file rename
7. `azure.yaml` name update
8. `dotnet build` validation on generated output

This keeps the POC narrow enough to finish quickly while proving the core point.
