# Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
#requires -Version 7.0
param(
    [ValidateSet('Inventory','Install','InstallPortable','InstallHermes','Register','Clean')][string]$Action = 'Inventory',
    [string]$AgentId,
    [string]$TestRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) '.work/cli-harness-tests-110')
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) '.work'))
$target = [IO.Path]::GetFullPath($TestRoot)
if (!$target.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '測試資料夾必須位於本儲存庫的 .work 子資料夾中。' }
$manifestPath = Join-Path $target 'manifest.json'
$definitions = @(
    @{ Id='claude'; Commands=@('claude'); Package='@anthropic-ai/claude-code' },
    @{ Id='codex'; Commands=@('codex'); Package='@openai/codex' },
    @{ Id='gemini'; Commands=@('gemini'); Package='@google/gemini-cli' },
    @{ Id='dsh'; Commands=@('dsh'); Package='@deepseek-ai/dsh' },
    @{ Id='kimi'; Commands=@('kimi') },
    @{ Id='qwen'; Commands=@('qwen'); Package='@qwen-code/qwen-code' },
    @{ Id='antigravity'; Commands=@('agy','agy_acp_server') },
    @{ Id='copilot'; Commands=@('copilot'); Package='@github/copilot' },
    @{ Id='cursor'; Commands=@('cursor-agent','agent') },
    @{ Id='opencode'; Commands=@('opencode'); Package='opencode-ai' },
    @{ Id='cline'; Commands=@('cline'); Package='cline' },
    @{ Id='codebuddy'; Commands=@('codebuddy'); Package='@tencent-ai/codebuddy-code' },
    @{ Id='qoder'; Commands=@('qoder','qodercli'); Package='@qoder-ai/qodercli' },
    @{ Id='trae'; Commands=@('traecli') },
    @{ Id='kiro'; Commands=@('kiro-cli') },
    @{ Id='pi'; Commands=@('pi'); Package='@mariozechner/pi-coding-agent' },
    @{ Id='devin'; Commands=@('devin') },
    @{ Id='grok'; Commands=@('grok'); Package='@xai-official/grok' },
    @{ Id='hermes'; Commands=@('hermes') },
    @{ Id='openclaw'; Commands=@('openclaw'); Package='openclaw' },
    @{ Id='minimax'; Commands=@('mcode'); Package='@minimax-ai/code' }
)
function Save-Manifest($value) { $value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8 }
if (!(Test-Path -LiteralPath $manifestPath)) {
    if ($Action -eq 'Clean') { throw '缺少測試歸屬清單，拒絕清理。' }
    if (Test-Path -LiteralPath $target) { throw '資料夾已存在但沒有歸屬清單，請改用新的測試資料夾。' }
    New-Item -ItemType Directory -Path $target | Out-Null
    $original = foreach ($entry in $definitions) {
        $found = foreach ($command in $entry.Commands) { Get-Command $command -All -ErrorAction SilentlyContinue | Where-Object { $_.Source } | Select-Object -ExpandProperty Source }
        @{ Id=$entry.Id; Paths=@($found | Select-Object -Unique) }
    }
    Save-Manifest @{ Owner='BigBlueFish-1.1.0-AgentTests'; Root=$target; Created=(Get-Date).ToString('o'); Existing=@($original); Installed=@(); OwnedDirectories=@() }
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.Owner -ne 'BigBlueFish-1.1.0-AgentTests' -or [IO.Path]::GetFullPath($manifest.Root) -ne $target) { throw '歸屬清單與實際資料夾不符。' }
if ($Action -eq 'Inventory') { $manifest.Existing | Select-Object Id,@{Name='ExistingPaths';Expression={$_.Paths -join ' | '}}; return }
if ($Action -eq 'InstallHermes') {
    $directory = Join-Path $target 'profiles/hermes'
    if ($manifest.OwnedDirectories -notcontains $directory) {
        if (Test-Path -LiteralPath $directory) { throw '資料夾不屬於本清單。' }
        New-Item -ItemType Directory -Path $directory | Out-Null
        $manifest.OwnedDirectories = @($manifest.OwnedDirectories) + $directory; Save-Manifest $manifest
    }
    $uv = (Get-Command uv -CommandType Application | Select-Object -First 1).Source
    # uv tool install does not inherit the upstream project's dependency overrides.
    $pyproject = (Invoke-WebRequest 'https://raw.githubusercontent.com/NousResearch/hermes-agent/main/pyproject.toml').Content
    $section = [regex]::Match($pyproject, '(?s)override-dependencies\s*=\s*\[(.*?)\r?\n\]')
    if (!$section.Success) { throw '缺少 Hermes 官方的相依套件覆寫宣告。' }
    $overrides = [regex]::Matches($section.Groups[1].Value, '(?m)^\s*"([^"]+)"').Groups | Where-Object Name -eq '1' | ForEach-Object Value
    $overridePath = Join-Path $directory 'official-overrides.txt'
    $overrides | Set-Content -LiteralPath $overridePath -Encoding utf8
    $start = [Diagnostics.ProcessStartInfo]::new($uv)
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    foreach ($entry in @{UV_CACHE_DIR='cache'; UV_TOOL_DIR='tools'; UV_TOOL_BIN_DIR='bin'; UV_PYTHON_INSTALL_DIR='python'; HERMES_HOME='user-state'}.GetEnumerator()) { $start.Environment[$entry.Key] = Join-Path $directory $entry.Value }
    $start.Environment['GIT_CONFIG_COUNT']='1'; $start.Environment['GIT_CONFIG_KEY_0']='core.longpaths'; $start.Environment['GIT_CONFIG_VALUE_0']='true'
    $source = Join-Path $directory 'source'
    if (!(Test-Path -LiteralPath (Join-Path $source 'pyproject.toml'))) {
        & git -c core.longpaths=true clone --depth 1 https://github.com/NousResearch/hermes-agent.git $source
        if ($LASTEXITCODE -ne 0) { throw 'Hermes 官方原始碼下載失敗。' }
    }
    foreach ($argument in @('tool','install','--python','3.14','--overrides',$overridePath,'--editable',($source+'[acp]'))) { $start.ArgumentList.Add($argument) }
    $process=[Diagnostics.Process]::Start($start); $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(300000)) { $process.Kill($true); throw 'Hermes 測試安裝逾時。' }
    $stdout.Result | Set-Content -LiteralPath (Join-Path $directory 'install.stdout.txt') -Encoding utf8
    $stderr.Result | Set-Content -LiteralPath (Join-Path $directory 'install.stderr.txt') -Encoding utf8
    if ($process.ExitCode -ne 0) { throw 'Hermes 測試安裝失敗，請看私有測試資料夾中的記錄。' }
    $manifest.Installed=@($manifest.Installed | Where-Object Id -NE 'hermes') + @{Id='hermes';Directory=$directory;Package='git+https://github.com/NousResearch/hermes-agent.git';At=(Get-Date).ToString('o')}; Save-Manifest $manifest
    Write-Output "獨立的 Hermes 測試安裝完成：$directory"; return
}
if ($Action -eq 'InstallPortable') {
    $registryIds = @{ antigravity='antigravity-acp'; cursor='cursor'; devin='devin'; trae='trae' }
    $registryId = $registryIds[$AgentId]
    if (!$registryId) { throw '此可攜式測試下載尚未列入清單。' }
    if (($manifest.Existing | Where-Object Id -EQ $AgentId).Paths.Count -gt 0) { Write-Output '沿用原有程式。'; return }
    $directory = Join-Path $target "agents/$AgentId"
    if ($manifest.OwnedDirectories -notcontains $directory) {
        if (Test-Path -LiteralPath $directory) { throw '資料夾不屬於本清單。' }
        New-Item -ItemType Directory -Path $directory | Out-Null
        $manifest.OwnedDirectories = @($manifest.OwnedDirectories) + $directory
        Save-Manifest $manifest
    }
    if ($AgentId -eq 'trae') {
        $record = Invoke-RestMethod -Uri 'https://lf-cdn.trae.com.cn/obj/trae-com-cn/trae-cli/v2/public/stable/latest.json'
        $artifact = $record.artifacts | Where-Object platform -EQ 'windows-x86_64' | Select-Object -First 1
        $artifact | Add-Member -NotePropertyName archive -NotePropertyValue $artifact.url
    } else {
        $record = Invoke-RestMethod -Uri "https://raw.githubusercontent.com/agentclientprotocol/registry/main/$registryId/agent.json"
        $artifact = $record.distribution.binary.'windows-x86_64'
    }
    $uri = [Uri]$artifact.archive
    if ($uri.Scheme -ne 'https' -or $uri.Host -notin @('dl.google.com','downloads.cursor.com','static.devin.ai','lf-cdn.trae.com.cn')) { throw '非預期的官方發行網址。' }
    $archive = Join-Path $directory 'artifact.zip'
    Invoke-WebRequest -Uri $uri -OutFile $archive -TimeoutSec 180
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    if ($artifact.sha256 -and $hash -ne $artifact.sha256) { throw '發行元件驗證失敗。' }
    if ($AgentId -eq 'trae') {
        $portable=Join-Path $directory 'portable'; New-Item -ItemType Directory -Path $portable -ErrorAction SilentlyContinue | Out-Null
        function Expand-TestGzip([string]$inputFile,[string]$outputFile) {
            $inputStream=[IO.File]::OpenRead($inputFile); $gzip=[IO.Compression.GZipStream]::new($inputStream,[IO.Compression.CompressionMode]::Decompress); $outputStream=[IO.File]::Create($outputFile)
            try { $gzip.CopyTo($outputStream) } finally { $outputStream.Dispose(); $gzip.Dispose(); $inputStream.Dispose() }
        }
        Expand-TestGzip $archive (Join-Path $portable 'traex.exe')
        foreach ($sidecar in $artifact.sidecars) {
            $sidecarUri=[Uri]$sidecar.url
            if ($sidecarUri.Scheme -ne 'https' -or $sidecarUri.Host -ne 'lf-cdn.trae.com.cn') { throw '非預期的 TRAE 元件網址。' }
            $sidecarFile=Join-Path $directory ($sidecar.name+'.gz'); Invoke-WebRequest -Uri $sidecarUri -OutFile $sidecarFile -TimeoutSec 180
            if ((Get-FileHash -LiteralPath $sidecarFile -Algorithm SHA256).Hash -ne $sidecar.sha256) { throw 'TRAE 元件驗證失敗。' }
            Expand-TestGzip $sidecarFile (Join-Path $portable 'traex-code-mode-host.exe')
        }
        ('@echo off'+"`r`n"+'"%~dp0traex.exe" %*'+"`r`n") | Set-Content -LiteralPath (Join-Path $portable 'traecli.cmd') -Encoding ascii
    } else { Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $directory 'portable') }
    $manifest.Installed = @($manifest.Installed | Where-Object Id -NE $AgentId) + @{ Id=$AgentId; Directory=$directory; Package=$uri.AbsoluteUri; Sha256=$hash; At=(Get-Date).ToString('o') }
    Save-Manifest $manifest
    Write-Output "可攜式測試安裝完成：$directory"
    return
}
if ($Action -eq 'Register') {
    if (!($definitions | Where-Object Id -EQ $AgentId)) { throw '請選擇支援的 AgentId。' }
    $directory = Join-Path $target "profiles/$AgentId"
    if ($manifest.OwnedDirectories -notcontains $directory) {
        if (Test-Path -LiteralPath $directory) { throw '不是本清單建立的資料夾。' }
        New-Item -ItemType Directory -Path $directory | Out-Null
        $manifest.OwnedDirectories = @($manifest.OwnedDirectories) + $directory
        Save-Manifest $manifest
    }
    Write-Output $directory
    return
}
if ($Action -eq 'Install') {
    $definition = $definitions | Where-Object Id -EQ $AgentId | Select-Object -First 1
    if (!$definition) { throw '請選擇支援的 AgentId。' }
    $existing = $manifest.Existing | Where-Object Id -EQ $AgentId
    if ($existing.Paths.Count -gt 0) { Write-Output "沿用原有的 $AgentId，不安裝、不升級：$($existing.Paths -join ', ')"; return }
    if (!$definition.Package) { throw '此產品須使用官方的獨立發行程式；不在主機上執行全域安裝程式。' }
    $node = (Get-Command node -CommandType Application | Select-Object -First 1).Source
    $npmCommand = (Get-Command npm.cmd -ErrorAction Stop | Select-Object -First 1).Source
    $npmScript = Join-Path (Split-Path $npmCommand -Parent) 'node_modules/npm/bin/npm-cli.js'
    if (!(Test-Path -LiteralPath $npmScript)) { throw '找不到可直接呼叫的 npm 指令碼。' }
    $destination = Join-Path $target "agents/$AgentId"
    $cache = Join-Path $target 'cache'
    foreach ($directory in @($destination,$cache)) {
        if ($manifest.OwnedDirectories -notcontains $directory) {
            if (Test-Path -LiteralPath $directory) { throw "不是本清單建立的資料夾，拒絕使用：$directory" }
            New-Item -ItemType Directory -Path $directory | Out-Null
            $manifest.OwnedDirectories = @($manifest.OwnedDirectories) + $directory
        }
    }
    Save-Manifest $manifest
    $start = [Diagnostics.ProcessStartInfo]::new($node)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.WorkingDirectory = $destination
    foreach ($argument in @($npmScript,'install','--prefix',$destination,'--cache',$cache,'--registry','https://registry.npmjs.org','--no-audit','--no-fund','--save-exact',$definition.Package)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(300000)) { $process.Kill($true); throw '測試安裝逾時，資料夾仍在清單中，可依清單清理。' }
    $stdout.Result | Set-Content -LiteralPath (Join-Path $destination 'install.stdout.txt') -Encoding utf8
    $stderr.Result | Set-Content -LiteralPath (Join-Path $destination 'install.stderr.txt') -Encoding utf8
    if ($process.ExitCode -ne 0) { throw "測試安裝失敗，結束代碼 $($process.ExitCode)，請看測試資料夾中的記錄。" }
    $manifest.Installed = @($manifest.Installed | Where-Object Id -NE $AgentId) + @{ Id=$AgentId; Directory=$destination; Package=$definition.Package; At=(Get-Date).ToString('o') }
    Save-Manifest $manifest
    Write-Output "獨立測試安裝完成：$destination"
    return
}
foreach ($directory in $manifest.OwnedDirectories) {
    $resolved = [IO.Path]::GetFullPath($directory)
    if (!$resolved.StartsWith($target + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw '清單包含越界的資料夾，拒絕清理。' }
    if (Test-Path -LiteralPath $resolved) {
        $item = Get-Item -LiteralPath $resolved
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "資料夾是重新剖析點，拒絕遞迴清理：$resolved" }
        # Git/Python test installs contain read-only or hidden files. Clear only those local attributes.
        foreach ($entry in @(Get-ChildItem -LiteralPath $resolved -Recurse -Force) + @($item)) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            $entry.Attributes = $entry.Attributes -band (-bnot ([IO.FileAttributes]::ReadOnly -bor [IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System))
        }
        Remove-Item -LiteralPath $resolved -Recurse
    }
}
$manifest.Installed=@(); $manifest.OwnedDirectories=@(); Save-Manifest $manifest
foreach ($entry in $manifest.Existing) {
    foreach ($path in $entry.Paths) { if (!(Test-Path -LiteralPath $path)) { throw "原有的 CLI 已無法存取：$path" } }
}
Write-Output '測試新增的安裝已清理；清單與原有的 CLI 保留。'
