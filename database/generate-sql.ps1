<#
.SYNOPSIS
    Generates the SQL scripts in this folder:
      01_schema.sql              - idempotent schema script built from all EF Core migrations
      02_seed_data.sql           - roles, demo catalog and store information exported from a seeded database
      FurnitureStoreDb_full.sql  - CREATE DATABASE + schema + seed, ready to run in SSMS / sqlcmd

.DESCRIPTION
    Run from any folder:  powershell -ExecutionPolicy Bypass -File database\generate-sql.ps1
    The seed export reads a database that the application has already seeded
    (by default the local development database on LocalDB).
    User accounts are NOT exported (they contain password hashes); the application creates
    the admin account at startup from the Seed:AdminPassword secret.

.PARAMETER SourceConnectionString
    Database to export seed data from.

.PARAMETER DatabaseName
    Database name used in the CREATE DATABASE / USE statements of the full script.
#>
param(
    [string]$SourceConnectionString = "Server=(localdb)\MSSQLLocalDB;Database=FurnitureStoreDb;Trusted_Connection=True;TrustServerCertificate=True",
    [string]$DatabaseName = "FurnitureStoreDb"
)

$ErrorActionPreference = "Stop"
$outDir = $PSScriptRoot
$solutionRoot = Split-Path -Parent $PSScriptRoot
$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

# ---------------------------------------------------------------- 1. Schema
Push-Location $solutionRoot
try {
    dotnet tool restore | Out-Null
    $schemaFile = Join-Path $outDir "01_schema.sql"
    dotnet tool run dotnet-ef migrations script --idempotent `
        --project src/FurnitureStore.Infrastructure `
        --startup-project src/FurnitureStore.Web `
        --output $schemaFile
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef migrations script failed." }
}
finally {
    Pop-Location
}

# ---------------------------------------------------------------- 2. Seed data
function Format-SqlValue($value) {
    if ($null -eq $value -or $value -is [System.DBNull]) { return "NULL" }
    switch ($value.GetType().FullName) {
        "System.String"   { return "N'" + $value.Replace("'", "''") + "'" }
        "System.Boolean"  { if ($value) { return "1" } else { return "0" } }
        "System.DateTime" { return "'" + $value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", $invariant) + "'" }
        "System.Guid"     { return "'" + $value.ToString() + "'" }
        "System.Byte[]"   { return "0x" + [System.BitConverter]::ToString($value).Replace("-", "") }
        default           { return [System.Convert]::ToString($value, $invariant) }
    }
}

function Export-Table($connection, [string]$table, [System.Text.StringBuilder]$sb, [string]$indent = "    ") {
    $identityCmd = $connection.CreateCommand()
    $identityCmd.CommandText = "SELECT OBJECTPROPERTY(OBJECT_ID(N'[dbo].[$table]'), 'TableHasIdentity')"
    $hasIdentity = [int]$identityCmd.ExecuteScalar() -eq 1

    $cmd = $connection.CreateCommand()
    $cmd.CommandText = "SELECT * FROM [dbo].[$table] ORDER BY 1"
    $reader = $cmd.ExecuteReader()
    try {
        $columns = @(for ($i = 0; $i -lt $reader.FieldCount; $i++) { "[" + $reader.GetName($i) + "]" })
        $columnList = $columns -join ", "
        $rows = 0
        if ($hasIdentity) { [void]$sb.AppendLine("$indent" + "SET IDENTITY_INSERT [dbo].[$table] ON;") }
        while ($reader.Read()) {
            $values = @(for ($i = 0; $i -lt $reader.FieldCount; $i++) { Format-SqlValue $reader.GetValue($i) })
            [void]$sb.AppendLine("$indent" + "INSERT INTO [dbo].[$table] ($columnList) VALUES (" + ($values -join ", ") + ");")
            $rows++
        }
        if ($hasIdentity) { [void]$sb.AppendLine("$indent" + "SET IDENTITY_INSERT [dbo].[$table] OFF;") }
        if ($afterInsert.ContainsKey($table)) { [void]$sb.AppendLine("$indent" + $afterInsert[$table]) }
        Write-Host ("  {0,-28} {1,5} rows" -f $table, $rows)
    }
    finally {
        $reader.Close()
    }
}

# Parents before children so foreign keys are satisfied.
$catalogTables = @(
    "ProductStyles", "ProductColors", "ProductMaterials", "ProductSizes", "Categories",
    "Products", "ProductVariants", "ProductImages",
    "ProductVariantColors", "ProductVariantMaterials", "ProductVariantSizes"
)
# Optional reference data added by later phases; exported only if the table exists.
$optionalTables = @("PriceRules", "AIKnowledgeEntries", "Coupons")

# Reviews, orders and coupon usages are not exported, so counters derived from them are reset
# (the application recalculates them when it seeds demo activity).
$afterInsert = @{
    "Products" = "UPDATE [dbo].[Products] SET [AverageRating] = 0, [ReviewCount] = 0;"
    "Coupons"  = "UPDATE [dbo].[Coupons] SET [UsedCount] = 0;"
}

$connection = New-Object System.Data.SqlClient.SqlConnection($SourceConnectionString)
$connection.Open()
try {
    $existsCmd = $connection.CreateCommand()
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("-- =====================================================================")
    [void]$sb.AppendLine("-- Nhà Mộc Furniture - seed data (roles, demo catalog, store information)")
    [void]$sb.AppendLine("-- Generated by database/generate-sql.ps1 on " + (Get-Date -Format "yyyy-MM-dd HH:mm"))
    [void]$sb.AppendLine("-- Run AFTER 01_schema.sql. Safe to run more than once: each block only")
    [void]$sb.AppendLine("-- inserts when its tables are still empty.")
    [void]$sb.AppendLine("-- User accounts are not included: the application creates the admin")
    [void]$sb.AppendLine("-- account at startup from the Seed:AdminPassword secret.")
    [void]$sb.AppendLine("-- =====================================================================")
    [void]$sb.AppendLine("SET ANSI_NULLS ON;")
    [void]$sb.AppendLine("SET QUOTED_IDENTIFIER ON;")
    [void]$sb.AppendLine("SET NOCOUNT ON;")
    [void]$sb.AppendLine("SET XACT_ABORT ON;")
    [void]$sb.AppendLine("BEGIN TRANSACTION;")
    [void]$sb.AppendLine("")

    Write-Host "Exporting seed data from source database:"

    # Roles: inserted one by one when missing (the app may already have created some).
    [void]$sb.AppendLine("-- Roles")
    $roleCmd = $connection.CreateCommand()
    $roleCmd.CommandText = "SELECT [Id], [Name], [NormalizedName], [ConcurrencyStamp], [Description] FROM [dbo].[AspNetRoles] ORDER BY [Name]"
    $roleReader = $roleCmd.ExecuteReader()
    $roleCount = 0
    while ($roleReader.Read()) {
        $vals = @(for ($i = 0; $i -lt 5; $i++) { Format-SqlValue $roleReader.GetValue($i) })
        [void]$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = " + $vals[2] + ")")
        [void]$sb.AppendLine("    INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp], [Description]) VALUES (" + ($vals -join ", ") + ");")
        $roleCount++
    }
    $roleReader.Close()
    Write-Host ("  {0,-28} {1,5} rows" -f "AspNetRoles", $roleCount)
    [void]$sb.AppendLine("")

    # Catalog: only when every catalog table is empty, so ids and foreign keys line up.
    $emptyChecks = ($catalogTables | ForEach-Object { "NOT EXISTS (SELECT 1 FROM [dbo].[$_])" }) -join "`r`n   AND "
    [void]$sb.AppendLine("-- Demo catalog")
    [void]$sb.AppendLine("IF $emptyChecks")
    [void]$sb.AppendLine("BEGIN")
    foreach ($table in $catalogTables) {
        [void]$sb.AppendLine("    -- $table")
        Export-Table $connection $table $sb
    }
    [void]$sb.AppendLine("END")
    [void]$sb.AppendLine("ELSE")
    [void]$sb.AppendLine("    PRINT N'Catalog tables already contain data - demo catalog skipped.';")
    [void]$sb.AppendLine("")

    foreach ($table in @("StoreInformation") + $optionalTables) {
        $existsCmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = N'$table'"
        if ([int]$existsCmd.ExecuteScalar() -eq 0) { continue }
        $existsCmd.CommandText = "SELECT COUNT(*) FROM [dbo].[$table]"
        if ([int]$existsCmd.ExecuteScalar() -eq 0) { continue }
        [void]$sb.AppendLine("-- $table")
        [void]$sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM [dbo].[$table])")
        [void]$sb.AppendLine("BEGIN")
        Export-Table $connection $table $sb
        [void]$sb.AppendLine("END")
        [void]$sb.AppendLine("")
    }

    [void]$sb.AppendLine("COMMIT TRANSACTION;")
    [void]$sb.AppendLine("PRINT N'Seed data applied.';")
    [void]$sb.AppendLine("GO")

    $seedFile = Join-Path $outDir "02_seed_data.sql"
    [System.IO.File]::WriteAllText($seedFile, $sb.ToString(), $utf8Bom)
}
finally {
    $connection.Close()
}

# ---------------------------------------------------------------- 3. Full script
$header = @"
-- =====================================================================
-- Nhà Mộc Furniture - full database script
-- Generated by database/generate-sql.ps1 on $(Get-Date -Format "yyyy-MM-dd HH:mm")
--   1. Creates database [$DatabaseName] if it does not exist
--   2. Creates / upgrades the schema (idempotent EF Core migrations script)
--   3. Inserts roles, demo catalog and store information (only into empty tables)
-- Run in SSMS, or:  sqlcmd -S <server> -E -f 65001 -i FurnitureStoreDb_full.sql
-- =====================================================================
IF DB_ID(N'$DatabaseName') IS NULL
    CREATE DATABASE [$DatabaseName];
GO
USE [$DatabaseName];
GO

"@

# Filtered indexes require QUOTED_IDENTIFIER ON; sqlcmd turns it OFF by default (SSMS turns it ON).
$sessionOptions = "SET ANSI_NULLS ON;`r`nSET QUOTED_IDENTIFIER ON;`r`nGO`r`n`r`n"
$schema = $sessionOptions + [System.IO.File]::ReadAllText((Join-Path $outDir "01_schema.sql")).TrimStart([char]0xFEFF)
$seed = [System.IO.File]::ReadAllText((Join-Path $outDir "02_seed_data.sql"))
$full = $header + $schema.TrimStart([char]0xFEFF) + "`r`nGO`r`n`r`n" + $seed.TrimStart([char]0xFEFF)
[System.IO.File]::WriteAllText((Join-Path $outDir "FurnitureStoreDb_full.sql"), $full, $utf8Bom)

# Make the schema file UTF-8 with BOM too, so SSMS shows Vietnamese correctly.
[System.IO.File]::WriteAllText((Join-Path $outDir "01_schema.sql"), $schema.TrimStart([char]0xFEFF), $utf8Bom)

Write-Host ""
Write-Host "Generated:"
Get-ChildItem $outDir -Filter *.sql | ForEach-Object { Write-Host ("  {0,-28} {1,10:N0} bytes" -f $_.Name, $_.Length) }
