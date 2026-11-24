param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [Parameter(Mandatory=$true)][string]$OutputDir,
    [switch]$TestDesk
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$output = [IO.Path]::GetFullPath($OutputDir)
if ($output -eq $root -or $output.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Choose an output directory outside the repository' }
$managed = Join-Path $GameDir 'Probably Stolen_Data/Managed'
$version = (dotnet --version).Trim()
$line = dotnet --list-sdks | Where-Object { $_.StartsWith("$version [") } | Select-Object -First 1
$sdkHome = $line.Substring($line.IndexOf('[')+1).TrimEnd(']')
$compiler = Join-Path $sdkHome "$version/Roslyn/bincore/csc.dll"
$target = Join-Path $output 'white_dragon_ledger'
[IO.Directory]::CreateDirectory($target) | Out-Null
$references = Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object { '/reference:'+$_.FullName }
$dll = Join-Path $target 'WhiteDragonLedger.dll'
$sources = (Get-ChildItem -LiteralPath (Join-Path $root 'mod') -Filter '*.cs').FullName
& dotnet $compiler /nologo /nostdlib+ /optimize+ /target:library "/out:$dll" $references $sources
if ($LASTEXITCODE) { throw 'Compilation failed' }
Copy-Item -LiteralPath (Join-Path $root 'mod/manifest.xml') -Destination $target
Write-Output $target

if ($TestDesk) {
    $test = Join-Path $output 'desk-replay'
    [IO.Directory]::CreateDirectory($test) | Out-Null
    $testRefs = 'mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','Newtonsoft.Json.dll' |
        ForEach-Object { '/reference:' + (Join-Path $managed $_) }
    $exe = Join-Path $test 'DeskReplay.exe'
    $testSources = 'mod/LiquidParts.cs','mod/Bargaining.cs','tests/BargainReplay.cs','mod/StockRows.cs','mod/BagStock.cs','tests/BagStockReplay.cs','mod/ItemDetails.cs','mod/InventorySorting.cs','tests/InventorySortingReplay.cs','mod/ItemMenu.cs','mod/ItemPanels.cs','mod/Chemist.cs','tests/ChemistReplay.cs','tests/ItemMenuReplay.cs','tests/ItemPanelReplay.cs','mod/ShopDesk.cs','mod/SaleOrder.cs','mod/TradeTools.cs','mod/WildNetwork.cs','tests/WildNetworkReplay.cs','tests/MixedTradeReplay.cs','tests/SaleResumeReplay.cs','mod/Move.cs','mod/CounterStock.cs','mod/CounterPapers.cs','mod/StockWindow.cs','mod/StockTransfer.cs','mod/PurifierNotice.cs','tests/StockWindowReplay.cs','mod/Counter.cs','mod/Barter.cs','tests/BarterReplay.cs','mod/CounterDialogue.cs','mod/NightBill.cs','tests/CounterDayReplay.cs','mod/PanelDemand.cs','tests/PanelDemandReplay.cs','mod/WaterPanel.cs','mod/WaterUse.cs','tests/WaterUseReplay.cs','tests/WaterPanelReplay.cs','mod/InformationReceipt.cs','tests/InformationReplay.cs','mod/Reception.cs','mod/Workstation.cs','mod/ShopDocuments.cs','mod/DocumentReaders.cs','tests/DocumentReplay.cs','tests/WorkstationReplay.cs','mod/Phone.cs','mod/Scenes.cs','tests/MachineWaitReplay.cs','mod/HeatControl.cs','tests/HeatControlReplay.cs','mod/MachineWait.cs','mod/CommandFiles.cs','mod/CommandMailbox.cs','mod/Journey.cs','mod/ScratchCard.cs','tests/ScratchCardReplay.cs','mod/FeedbackBox.cs','tests/FeedbackReplay.cs','mod/Specializations.cs','mod/StartingPerks.cs','mod/StartingDifficulty.cs','tests/DifficultyReplay.cs','tests/PerkReplay.cs','tests/SpecializationReplay.cs','mod/ShopActivity.cs','mod/Exploration.cs','tests/JourneyReplay.cs','mod/TravelOptions.cs','mod/NativeButton.cs','tests/TravelOptionsReplay.cs','mod/ShopDay.cs','mod/Calendar.cs','tests/DayReplay.cs','tests/SceneReplay.cs','tests/PhoneReplay.cs','tests/DeskReplay.cs' | ForEach-Object { Join-Path $root $_ }
    & dotnet $compiler /nologo /nostdlib+ /target:exe "/out:$exe" $testRefs $testSources
    if ($LASTEXITCODE) { throw 'Desk replay compilation failed' }
    Copy-Item -LiteralPath (Join-Path $managed 'Newtonsoft.Json.dll') -Destination $test -Force
    & $exe
    if ($LASTEXITCODE) { throw 'Desk replay failed' }
}
