$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$output = Join-Path $projectRoot 'Temp/HideOutModelChecks'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$model = Join-Path $projectRoot 'Assets/Scenes/HideOutSystemScene/Scripts/HideOutModel.cs'
$checks = Join-Path $PSScriptRoot 'ModelChecks.cs.txt'
$project = Join-Path $output 'ModelChecks.csproj'
$xml = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="' + $model + '"/><Compile Include="' + $checks + '"/></ItemGroup></Project>'
[IO.File]::WriteAllText($project, $xml)
dotnet run --project $project --verbosity quiet
exit $LASTEXITCODE
