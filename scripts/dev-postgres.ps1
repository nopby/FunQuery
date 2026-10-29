[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'stop', 'reset', 'seed', 'status')]
    [string]$Action = 'start'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$root = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $root 'deploy/compose.yaml'
$seedFile = Join-Path $root 'db/seed.sql'
$connectionString =
    'Host=127.0.0.1;Port=5433;Username=postgres;Password=postgres;Database=postgres'

function Test-ComposeProvider {
    param(
        [string]$Exe,
        [string[]]$Prefix
    )

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'

    try {
        $commandArgs = @($Prefix) + @('version')
        & $Exe @commandArgs 2>&1 | Out-Null
        return $LASTEXITCODE -eq 0
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Resolve-Compose {
    if (Get-Command podman-compose -ErrorAction SilentlyContinue) {
        return @{ Exe = 'podman-compose'; Prefix = @() }
    }

    if (Get-Command docker-compose -ErrorAction SilentlyContinue) {
        return @{ Exe = 'docker-compose'; Prefix = @() }
    }

    if (Get-Command podman -ErrorAction SilentlyContinue) {
        if (Test-ComposeProvider -Exe 'podman' -Prefix @('compose')) {
            return @{ Exe = 'podman'; Prefix = @('compose') }
        }
    }

    if (Get-Command docker -ErrorAction SilentlyContinue) {
        if (Test-ComposeProvider -Exe 'docker' -Prefix @('compose')) {
            return @{ Exe = 'docker'; Prefix = @('compose') }
        }
    }

    throw 'No Compose provider found. Install Podman with a compose provider, or Docker Compose.'
}

$compose = Resolve-Compose

function Invoke-Compose {
    param([string[]]$Arguments)

    $commandArgs = @($compose.Prefix) + @('-f', $composeFile) + $Arguments
    & $compose.Exe @commandArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Compose command failed (exit $LASTEXITCODE): " +
            "$($compose.Exe) $($commandArgs -join ' ')"
    }
}

function Wait-ForPostgres {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $commandArgs = @($compose.Prefix) + @(
            '-f', $composeFile,
            'exec', '-T', 'postgres',
            'pg_isready', '-h', '127.0.0.1', '-U', 'postgres', '-d', 'postgres'
        )

        & $compose.Exe @commandArgs *> $null
        if ($LASTEXITCODE -eq 0) {
            return
        }

        Start-Sleep -Seconds 2
    }

    throw 'PostgreSQL did not become ready within 60 seconds. Check the container logs.'
}

function Start-Postgres {
    Invoke-Compose -Arguments @('up', '-d')
    Wait-ForPostgres

    Write-Host 'PostgreSQL is ready.'
    Write-Host "  `$env:FUNQUERY_POSTGRES = '$connectionString'"
}

function Seed-Postgres {
    if (-not (Test-Path $seedFile)) {
        throw "Seed file not found: $seedFile"
    }

    $commandArgs = @($compose.Prefix) + @(
        '-f', $composeFile,
        'exec', '-T', 'postgres',
        'psql', '-U', 'postgres', '-d', 'postgres',
        '-v', 'ON_ERROR_STOP=1', '-f', '-'
    )

    Get-Content -Raw $seedFile | & $compose.Exe @commandArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Seeding failed (exit $LASTEXITCODE)."
    }

    Write-Host 'Seeded orders with 5 rows.'
}

switch ($Action) {
    'start' {
        Start-Postgres
    }

    'stop' {
        Invoke-Compose -Arguments @('down', '--remove-orphans')
        Write-Host 'PostgreSQL stopped; data volume kept.'
    }

    'reset' {
        Invoke-Compose -Arguments @('down', '-v', '--remove-orphans')
        Start-Postgres
        Seed-Postgres
    }

    'seed' {
        Seed-Postgres
    }

    'status' {
        Invoke-Compose -Arguments @('ps')
    }
}

exit 0
