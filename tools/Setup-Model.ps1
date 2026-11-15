param(
    [ValidateSet('HuggingFace','ModelScope','Manual','None')][string]$Source='HuggingFace',
    [string]$ModelFile,
    [string]$DestinationDirectory=$PSScriptRoot,
    [switch]$AllowUnverifiedModel
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Model-Helpers.ps1"
$DestinationDirectory=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestinationDirectory)
$DestinationDirectory=Join-Path $DestinationDirectory 'data'
if($ModelFile -and !$PSBoundParameters.ContainsKey('Source')){$Source='Manual'}
if($ModelFile -and $Source -ne 'Manual'){throw '-ModelFile 只用於手動安裝，請使用 -Source Manual。'}
if($Source -eq 'None'){
    Save-ModelSelection $DestinationDirectory ([ordered]@{Source='none';FileName=$null;Sha256=$null})
    Write-Host '已選擇無模型啟動。'
    return
}
$sources=Get-ModelSources
$folder=Join-Path $DestinationDirectory 'models'
if($Source -eq 'Manual'){
    if(!$ModelFile){$ModelFile=Read-Host '請輸入 GGUF 模型的完整路徑'}
    $ModelFile=(Resolve-Path -LiteralPath $ModelFile).Path
    if(!(Test-ModelGguf $ModelFile)){throw '請選擇有效的 GGUF 模型檔案。'}
    $hash=(Get-FileHash -LiteralPath $ModelFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $known=$sources | Where-Object Sha256 -EQ $hash | Select-Object -First 1
    if(!$known){
        Write-Warning '這不是本軟體期待的模型，您可以嘗試使用，但穩定性無法保證'
        if(!$AllowUnverifiedModel -and (Read-Host '繼續使用？輸入 Y 確認') -ine 'Y'){throw '已取消手動安裝。'}
    }
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $name=[IO.Path]::GetFileName($ModelFile)
    $target=Join-Path $folder $name
    if($ModelFile -ine $target){
        $temporary=$target+'.importing'
        Copy-Item -LiteralPath $ModelFile -Destination $temporary
        if(!(Test-ModelGguf $temporary) -or (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ine $hash){throw '模型檔案複製未完成，請重試。'}
        if([IO.File]::Exists($target)){[IO.File]::Replace($temporary,$target,[NullString]::Value)}else{[IO.File]::Move($temporary,$target)}
    }
    $id=if($known){$known.Id}else{'manual'}
}else{
    $artifact=if($Source -eq 'ModelScope'){$sources[1]}else{$sources[0]}
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $name=$artifact.FileName
    $target=Join-Path $folder $name
    $hash=$artifact.Sha256
    if(Test-Path -LiteralPath $target){
        if(!(Test-ModelGguf $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ine $hash){
            throw "目標位置已有不同的模型檔案，請檢查後重試，或使用手動安裝：$target"
        }
    }else{
        $temporary=$target+'.partial'
        Write-Host "正在從$($artifact.Label)下載本機 4B 模型：$($artifact.Page)"
        # A retry replaces only the incomplete download; the selection is committed after validation.
        Invoke-WebRequest -Uri $artifact.Url -OutFile $temporary
        if(!(Test-ModelGguf $temporary) -or (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ine $hash){throw '模型驗證失敗，保留 .partial 檔案供檢查。'}
        [IO.File]::Move($temporary,$target)
    }
    $id=$artifact.Id
}
Save-ModelSelection $DestinationDirectory ([ordered]@{Source=$id;FileName=$name;Sha256=$hash})
Write-Host "本機模型已就緒：$target"
