$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.SelectVoice('Microsoft Huihui Desktop')
$synth.Rate = 1
$items = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'narration.json') -Raw -Encoding UTF8 | ConvertFrom-Json
for ($i = 0; $i -lt $items.Count; $i++) {
    $synth.SetOutputToWaveFile((Join-Path $PSScriptRoot "voice-$i.wav"))
    $synth.Speak($items[$i].text)
    $synth.SetOutputToNull()
}
$synth.Dispose()
