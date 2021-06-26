$ProjectPath = Join-Path $PSScriptRoot "ParallelizeTests" "ParallelizeTests.csproj" -Resolve
$TestsRootPath = Join-Path $PSScriptRoot ".." -Resolve

dotnet run $ProjectPath -- "$TestsRootPath"
exit $LASTEXITCODE