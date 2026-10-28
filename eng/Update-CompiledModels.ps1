<#
.SYNOPSIS
    Regenerates the AOT sample's EF Core compiled models, one per target framework.

.DESCRIPTION
    Native AOT cannot build the EF Core model at runtime, so the sample ships compiled models. Run this after changing
    the sample's model. EF Core 9 and 10 models are generated with --nativeaot, which they need to track entities and
    save changes under Native AOT. EF Core 8 has no such option, so under Native AOT it can neither track nor save, and
    KetPsi rejects tracking queries there with a clear error. Requires the dotnet-ef tool.
#>
[CmdletBinding()]
param()

# Native commands write warnings to stderr; failures are detected from their exit codes instead.
$ErrorActionPreference = 'Continue'

$project = Join-Path $PSScriptRoot '..\samples\KetPsi.EntityFrameworkCore.AotSample\KetPsi.EntityFrameworkCore.AotSample.csproj'
$folders = @{ 'net8.0' = 'Net8'; 'net9.0' = 'Net9'; 'net10.0' = 'Net10' }

# The project file reads this to reference the Design package and leave out the models being regenerated.
$env:KetPsiDesignTime = 'true'
try {
    # dotnet ef builds but does not restore, and the Design package is only restored at design time.
    & dotnet restore $project
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet restore failed'
    }

    foreach ($framework in $folders.Keys) {
        $output = Join-Path 'CompiledModels' $folders[$framework]
        $arguments = @(
            'ef', 'dbcontext', 'optimize',
            '--project', $project,
            '--framework', $framework,
            '--context', 'ShopDb',
            '--output-dir', $output,
            '--namespace', 'AotSample.CompiledModel')
        if ($framework -ne 'net8.0') {
            $arguments += '--nativeaot'
        }

        Write-Host "Generating the compiled model for $framework"
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet ef failed for $framework"
        }
    }
}
finally {
    Remove-Item Env:KetPsiDesignTime
}
