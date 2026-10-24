param([Parameter(Mandatory=$true)][string]$InputFile,[Parameter(Mandatory=$true)][string]$OutputFile)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$extension=[IO.Path]::GetExtension($InputFile).ToLowerInvariant()
$application=$null;$document=$null;$previousSecurity=$null;$previousAlerts=$null;$createdApplication=$false
$beforeProcesses=@(Get-Process -Name WINWORD,POWERPNT,EXCEL,wps,wpp,et -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
try {
  if ($extension -in @('.doc','.docx','.docm','.rtf','.odt')) {
    $application=New-Object -ComObject Word.Application
    if ($application.Documents.Count -gt 0) { throw 'Cannot use standalone document preview process' }
    $previousSecurity=$application.AutomationSecurity;$application.AutomationSecurity=3
    $previousAlerts=$application.DisplayAlerts;$application.DisplayAlerts=0
    $missing=[Type]::Missing
    $document=$application.Documents.Open($InputFile,$false,$true,$false,$missing,$missing,$false,$missing,$missing,$missing,$missing,$false)
    $document.ExportAsFixedFormat($OutputFile,17)
    $document.Close(0);$document=$null
  } elseif ($extension -in @('.ppt','.pptx','.pptm','.pps','.ppsx','.odp')) {
    $application=New-Object -ComObject PowerPoint.Application
    if ($application.Presentations.Count -gt 0) { throw 'Cannot use standalone presentation preview process' }
    $previousSecurity=$application.AutomationSecurity;$application.AutomationSecurity=3
    $previousAlerts=$application.DisplayAlerts;$application.DisplayAlerts=1
    $document=$application.Presentations.Open($InputFile,-1,0,0)
    try { $document.ExportAsFixedFormat($OutputFile,2) } catch { $document.SaveAs($OutputFile,32) }
    $document.Close();$document=$null
  } elseif ($extension -in @('.xls','.xlsx','.xlsm','.ods')) {
    $application=New-Object -ComObject Excel.Application
    if ($application.Workbooks.Count -gt 0) { throw 'Cannot use standalone table preview process' }
    $previousSecurity=$application.AutomationSecurity;$application.AutomationSecurity=3
    $previousAlerts=$application.DisplayAlerts;$application.DisplayAlerts=$false
    $application.AskToUpdateLinks=$false
    $document=$application.Workbooks.Open($InputFile,0,$true)
    $document.ExportAsFixedFormat(0,$OutputFile)
    $document.Close($false);$document=$null
  } else { throw 'This document format is not supported' }
  if (-not (Test-Path -LiteralPath $OutputFile)) { throw 'Document preview was not generated' }
  [pscustomobject]@{Success=$true;Output=$OutputFile} | ConvertTo-Json -Compress
} catch {
  [pscustomobject]@{Success=$false;Error=$_.Exception.Message} | ConvertTo-Json -Compress
  exit 1
} finally {
  if ($document) { try { $document.Close(0) } catch {} }
  if ($application) {
    try { if ($null -ne $previousSecurity) { $application.AutomationSecurity=$previousSecurity }; if ($null -ne $previousAlerts) { $application.DisplayAlerts=$previousAlerts } } catch {}
    $newProcesses=@(Get-Process -Name WINWORD,POWERPNT,EXCEL,wps,wpp,et -ErrorAction SilentlyContinue | Where-Object { $_.Id -notin $beforeProcesses })
    if ($newProcesses.Count) { try { $application.Quit() } catch {} }
    try { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($application) } catch {}
  }
}
