$ErrorActionPreference = 'Stop'
$v = New-Object -ComObject SAPI.SpVoice
$v.Voice = $v.GetVoices().Item(0)
$v.Rate = 1
$items = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'narration.json') -Raw -Encoding UTF8 | ConvertFrom-Json
for ($i = 0; $i -lt $items.Count; $i++) {
 $stream = New-Object -ComObject SAPI.SpFileStream
 $stream.Open((Join-Path $PSScriptRoot "voice-$i.wav"),3,$false)
 $v.AudioOutputStream = $stream
 [void]$v.Speak($items[$i].text)
 $stream.Close()
 Write-Output "Voice $i ready"
}
