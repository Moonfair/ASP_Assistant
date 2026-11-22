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
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (Get-Process -Id $manifest.ProcessId -ErrorAction SilentlyContinue) {
        if ([DateTime]::UtcNow -ge $deadline) { throw '原程序尚未退出，更新已取消。' }
        Start-Sleep -Milliseconds 500
    }

    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path $jobDirectory 'setup.exe'
    $start.WorkingDirectory = $jobDirectory
    $start.UseShellExecute = $false
    $wizard = [System.Diagnostics.Process]::Start($start)
    try {
        $wizard.WaitForExit()
        # Inno Setup returns 2 or 5 when the user cancels the wizard.
        if ($wizard.ExitCode -notin @(0, 2, 5)) { throw "安装向导退出，错误码 $($wizard.ExitCode)。" }
    } finally {
        $wizard.Dispose()
    }
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
