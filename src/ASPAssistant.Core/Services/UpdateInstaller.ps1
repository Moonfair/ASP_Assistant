param([switch]$ShowErrorDialog)

$ErrorActionPreference = 'Stop'
$jobDirectory = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$updateRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'ASPAssistant-Update'))
$jobId = [Guid]::Empty
if ([IO.Path]::GetDirectoryName($jobDirectory) -ne $updateRoot -or
    -not [Guid]::TryParseExact([IO.Path]::GetFileName($jobDirectory), 'N', [ref]$jobId)) {
    throw 'Invalid update job directory.'
}

$exitCode = 1
$manifest = $null
try {
    $manifest = Get-Content -LiteralPath (Join-Path $jobDirectory 'update.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $source = $manifest.SourceDirectory
    $destination = $manifest.AppDirectory
    $exePath = Join-Path $destination 'ASPAssistant.App.exe'
    if (-not (Test-Path -LiteralPath (Join-Path $source 'ASPAssistant.App.exe') -PathType Leaf)) {
        throw '更新包缺少程序文件。'
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (Get-Process -Id $manifest.ProcessId -ErrorAction SilentlyContinue) {
        if ([DateTime]::UtcNow -ge $deadline) { throw '原程序尚未退出，更新已取消。' }
        Start-Sleep -Milliseconds 500
    }

    # Enumerate literal entries, so brackets/dollars/quotes in every path stay data.
    Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force -ErrorAction Stop
    }
    # Start-Process resolves PowerShell provider paths; ProcessStartInfo keeps these literal too.
    $restart = New-Object System.Diagnostics.ProcessStartInfo
    $restart.FileName = $exePath
    $restart.WorkingDirectory = $destination
    $restart.UseShellExecute = $false
    $restart.CreateNoWindow = $true
    [System.Diagnostics.Process]::Start($restart).Dispose()
    $exitCode = 0
} catch {
    $failure = $_.Exception.ToString()
    try {
        if ($manifest) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($manifest.ErrorLogPath)) | Out-Null
            [IO.File]::AppendAllText($manifest.ErrorLogPath, "[$([DateTime]::Now.ToString('s'))] $failure`r`n")
        }
    } catch { Write-Warning 'Could not write update failure log.' }
    Write-Error $failure -ErrorAction Continue
    if ($ShowErrorDialog) {
        try {
            Add-Type -AssemblyName System.Windows.Forms
            [System.Windows.Forms.MessageBox]::Show("更新失败，请重新下载完整发布包。`r`n$failure", 'ASPAssistant 更新失败') | Out-Null
        } catch { Write-Warning 'Could not show update failure dialog.' }
    }
} finally {
    # The validated GUID job is ours; never remove the shared update root or other jobs.
    try { Remove-Item -LiteralPath $jobDirectory -Recurse -Force -ErrorAction Stop }
    catch { Write-Warning "Could not clean update job: $_" }
}
exit $exitCode
