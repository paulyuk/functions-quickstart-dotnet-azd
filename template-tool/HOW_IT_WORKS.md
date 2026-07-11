# How the Template Tool Works

This file explains the prototype mechanics. Start with [README.md](./README.md) if you just want to run the tool.

## Intent

The POC treats a normal runnable AZD sample as the template source of truth.

The goal is to cover the common path with conventions and tiny local hints instead of a large maintained `template.json` manifest.

## Current target

This first implementation targets:

- .NET 10
- Azure Functions isolated worker
- C#
- a single `.sln`
- a single `.csproj`
- `azure.yaml`
- existing AZD `infra` parameterization

## Source resolution

The tool resolves `--source` in this order:

1. Existing local folder.
2. GitHub URL.
3. GitHub `owner/repo` shorthand.
4. Azure Samples template name.

For example:

```text
functions-quickstart-dotnet-azd
```

resolves to:

```text
https://github.com/Azure-Samples/functions-quickstart-dotnet-azd.git
```

Remote sources are cloned with:

```pwsh
git clone --depth 1
```

The clone is stored in a temporary folder and deleted after the command completes.

## Analyze mode

`analyze` reads the source and reports detected identity values.

It detects:

- project name
- project folder
- `.csproj`
- `.sln`
- C# namespace
- Azure Functions names from `[Function("...")]`
- `azure.yaml` `name`
- first launch profile in `Properties\launchSettings.json`

This output is diagnostic. It is not a template manifest.

## Plan mode

`plan` combines detected values with requested values and prints intended changes.

It does not write files.

The plan is meant to be readable by a human before using `apply`.

## Apply mode

`apply` does the work:

1. Resolves the source.
2. Copies the source to `--output`.
3. Renames the project folder.
4. Renames the `.csproj`.
5. Updates the `.sln` project name and project path.
6. Replaces the detected namespace in C# files.
7. Updates `azure.yaml` `name`.
8. Updates the `azure.yaml` service project path when it matches the old project folder.
9. Updates the launch profile name when it matches the old project identity.
10. Runs `dotnet build` in the generated output folder.

## Performance considerations

The prototype is fast because it does not try to interpret every file as a template.

The main performance choices are:

- It starts from a small set of likely files: `.sln`, `.csproj`, C# files, `azure.yaml`, and `launchSettings.json`.
- It skips generated folders like `bin`, `obj`, and `out`.
- It skips the tool folder.
- It does not scan docs for token replacement.
- It does not scan `infra` for token replacement because AZD already parameterizes it.
- Remote sources use `git clone --depth 1` so the tool does not download full history.

For this repo, the expensive step is usually not detection or replacement. It is cloning a remote source and running `dotnet build`.

Things to watch as the tool grows:

- Do not add whole-repo text replacement by default.
- Keep each language pack scoped to the files that matter for that language.
- Prefer exact conventions over broad regex searches.
- Keep generated folders excluded.
- Add caching only if remote clone time becomes a real problem.
- Keep `apply` writing to a new output folder so the tool does not need expensive rollback logic.

The intended model is small targeted inspection, then small targeted edits.

## Excluded folders

The copy and scan logic skips generated and tool folders:

- `.git`
- `.vs`
- `bin`
- `obj`
- `out`
- `template-tool`

## Why no README tokenization

Documentation is skipped by default because README text often explains the original sample, not app identity.

If documentation customization becomes necessary, it should be an explicit later-phase feature rather than automatic tokenization.

## Why no infra tokenization

The AZD `infra` folder is already designed to be parameterized through AZD environment values and deployment parameters.

The current POC leaves `infra` alone unless a later phase adds explicit local hints.

## Current limits

This is intentionally narrow:

- .NET 10 Azure Functions only for now
- one main C# project
- one solution file
- no custom function renaming yet
- no multi-project orchestration yet
- no language packs yet
- no VS Code template engine compatibility layer yet

The next phases should add language-specific convention packs without turning the tool into another large manifest format.
