# 从 SupportDocs 白名单 15/19/08 生成 docs/企微客户问答库.docx
# Windows PowerShell 5.1。不读指令目录，不改源文件，不进 MES。
#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$SupportDocsRoot = '',
    [string]$DocxPath = '',
    [string]$LogPath = '',
    [switch]$RegisterTask
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SupportDocsRoot)) {
    $SupportDocsRoot = Join-Path $RepoRoot 'SupportDocs'
}
if ([string]::IsNullOrWhiteSpace($DocxPath)) {
    $DocxPath = Join-Path $RepoRoot 'docs\企微客户问答库.docx'
}
if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $LogPath = Join-Path $PSScriptRoot 'wecom-qa.log'
}

$NeverNames = @(
    '09-MCP配置（需暗语）.md',
    '10-系统能力50问.md',
    '13-微聚轻MES客户现场问答速查.md',
    '14-王雩销售应答50问.md',
    '16-自动派工应答口径.md',
    '17-AI应答行为规则.md',
    '18-企业微信机器人角色设定模板.md',
    '_retrieval.json'
)

$Whitelist = @(
    @{ File = '15-智能客服纯净50问.md'; Kind = 'num'; Priority = 30 },
    @{ File = '19-AI功能价值与卖点问答.md'; Kind = 'ask'; Priority = 20 },
    @{ File = '08-扫码报工问答.md'; Kind = 'q'; Priority = 10 }
)

function Write-Log {
    param(
        [string]$Level,
        [string]$File,
        [string]$Step,
        [string]$Message
    )
    $line = '{0:yyyy-MM-dd HH:mm:ss} [{1}] 文件={2} 步骤={3} {4}' -f (Get-Date), $Level, $File, $Step, $Message
    $dir = Split-Path -Parent $LogPath
    if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
    Write-Host $line
}

function Read-Utf8Text {
    param([string]$Path)
    $reader = New-Object System.IO.StreamReader($Path, $true)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Close()
    }
}

function Get-QuestionKey {
    param([string]$Question)
    $t = [regex]::Replace($Question, '\s+', ' ').Trim()
    while ($t.Length -gt 0) {
        $last = $t.Substring($t.Length - 1)
        if ($last -eq '?' -or $last -eq ([string][char]0xFF1F) -or $last -eq ([string][char]0x3002)) {
            $t = $t.Substring(0, $t.Length - 1).TrimEnd()
        }
        else {
            break
        }
    }
    return $t
}

function Format-CustomerText {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    $s = $Text -replace '\*\*', ''
    $s = $s -replace "`r`n", "`n"
    $s = $s -replace "`r", "`n"
    $lines = $s -split "`n", -1
    $kept = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        $kept.Add($line.TrimEnd())
    }
    $s = ($kept -join "`n").Trim()
    $s = [regex]::Replace($s, "(`n){3,}", "`n`n")
    return $s
}

function Remove-InternalCallout {
    param([string]$Answer)
    $lines = $Answer -split "`n", -1
    $kept = New-Object System.Collections.Generic.List[string]
    $dropped = $false
    foreach ($line in $lines) {
        $isCallout = $false
        if ($line -match '^\s*>') {
            if ($line -match '16-自动派工应答口径' -or $line -match '不要念' -or $line -match '原样念') {
                $isCallout = $true
            }
        }
        if ($isCallout) {
            $dropped = $true
            continue
        }
        $kept.Add($line)
    }
    $text = Format-CustomerText (($kept -join "`n"))
    return @{ Text = $text; Dropped = $dropped }
}

function Get-Slice {
    param([string]$Text, [int]$Start, [int]$End)
    if ($End -lt $Start) { $End = $Start }
    $chunk = $Text.Substring($Start, $End - $Start)
    $h2 = [regex]::Match($chunk, '(?m)^##\s+')
    if ($h2.Success) {
        $chunk = $chunk.Substring(0, $h2.Index)
    }
    $hr = [regex]::Match($chunk, '(?m)^---\s*$')
    if ($hr.Success) {
        $chunk = $chunk.Substring(0, $hr.Index)
    }
    $rule = [regex]::Match($chunk, '(?m)^\*\*\*\s*$')
    if ($rule.Success) {
        $chunk = $chunk.Substring(0, $rule.Index)
    }
    return $chunk
}

function Get-AnswerFromMark {
    param([string]$Chunk)
    $m = [regex]::Match($Chunk, '(?m)^A[:：]\s*(.*)$')
    if (-not $m.Success) {
        return $null
    }
    $first = $m.Groups[1].Value
    $rest = ''
    $restAt = $m.Index + $m.Length
    if ($restAt -lt $Chunk.Length) {
        $rest = $Chunk.Substring($restAt)
    }
    return Format-CustomerText ($first + "`n" + $rest)
}

function Parse-Numbered {
    param([string]$Text, [string]$Source, [int]$Priority, [ref]$Ord)
    $items = New-Object System.Collections.Generic.List[object]
    $found = [regex]::Matches($Text, '(?m)^###\s+(\d+)\.\s+(.+?)\s*$')
    if ($found.Count -eq 0) {
        throw '解析失败：没有 ### 数字. 问句'
    }
    for ($i = 0; $i -lt $found.Count; $i++) {
        $m = $found[$i]
        $start = $m.Index + $m.Length
        $end = $Text.Length
        if ($i + 1 -lt $found.Count) { $end = $found[$i + 1].Index }
        $chunk = Get-Slice -Text $Text -Start $start -End $end
        $question = Format-CustomerText $m.Groups[2].Value
        $answer = Get-AnswerFromMark -Chunk $chunk
        if ([string]::IsNullOrWhiteSpace($question) -or [string]::IsNullOrWhiteSpace($answer)) {
            Write-Log -Level 'WARN' -File $Source -Step '解析' -Message ('第{0}条没有问句或答案，已跳过' -f $m.Groups[1].Value)
            continue
        }
        $Ord.Value = $Ord.Value + 1
        $items.Add([pscustomobject]@{
            Question = $question
            Answer   = $answer
            Source   = $Source
            Priority = $Priority
            Ord      = $Ord.Value
            Key      = (Get-QuestionKey $question)
        })
    }
    return $items
}

function Parse-Ask {
    param([string]$Text, [string]$Source, [int]$Priority, [ref]$Ord)
    $items = New-Object System.Collections.Generic.List[object]
    $found = [regex]::Matches($Text, '(?m)^\*\*问[:：]\s*(.+?)\*\*\s*$')
    if ($found.Count -eq 0) {
        throw '解析失败：没有 **问：** 条目'
    }
    for ($i = 0; $i -lt $found.Count; $i++) {
        $m = $found[$i]
        $start = $m.Index + $m.Length
        $end = $Text.Length
        if ($i + 1 -lt $found.Count) { $end = $found[$i + 1].Index }
        $chunk = Get-Slice -Text $Text -Start $start -End $end
        $question = Format-CustomerText $m.Groups[1].Value
        $answer = Get-AnswerFromMark -Chunk $chunk
        if ([string]::IsNullOrWhiteSpace($question) -or [string]::IsNullOrWhiteSpace($answer)) {
            Write-Log -Level 'WARN' -File $Source -Step '解析' -Message ('第{0}条没有问句或答案，已跳过' -f ($i + 1))
            continue
        }
        $Ord.Value = $Ord.Value + 1
        $items.Add([pscustomobject]@{
            Question = $question
            Answer   = $answer
            Source   = $Source
            Priority = $Priority
            Ord      = $Ord.Value
            Key      = (Get-QuestionKey $question)
        })
    }
    return $items
}

function Parse-QHeading {
    param([string]$Text, [string]$Source, [int]$Priority, [ref]$Ord)
    $sec = [regex]::Match($Text, '(?ms)^##\s*二、高情商问答[^\r\n]*\r?\n(.*?)(?=^##\s|\z)')
    if (-not $sec.Success) {
        throw '解析失败：找不到「二、高情商问答」'
    }
    $body = $sec.Groups[1].Value
    $found = [regex]::Matches($body, '(?m)^###\s+Q(\d+)\s+(.+?)\s*$')
    if ($found.Count -eq 0) {
        throw '解析失败：「二、高情商问答」里没有 ### Q 条目'
    }
    $items = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $found.Count; $i++) {
        $m = $found[$i]
        $start = $m.Index + $m.Length
        $end = $body.Length
        if ($i + 1 -lt $found.Count) { $end = $found[$i + 1].Index }
        $chunk = Get-Slice -Text $body -Start $start -End $end
        $cleaned = Remove-InternalCallout -Answer $chunk
        if ($cleaned.Dropped) {
            Write-Log -Level 'WARN' -File $Source -Step '解析' -Message ('Q{0} 已去掉点名其他文件的内部引用块' -f $m.Groups[1].Value)
        }
        $question = Format-CustomerText $m.Groups[2].Value
        $answer = $cleaned.Text
        if ([string]::IsNullOrWhiteSpace($question) -or [string]::IsNullOrWhiteSpace($answer)) {
            Write-Log -Level 'WARN' -File $Source -Step '解析' -Message ('Q{0} 没有问句或答案，已跳过' -f $m.Groups[1].Value)
            continue
        }
        $Ord.Value = $Ord.Value + 1
        $items.Add([pscustomobject]@{
            Question = $question
            Answer   = $answer
            Source   = $Source
            Priority = $Priority
            Ord      = $Ord.Value
            Key      = (Get-QuestionKey $question)
        })
    }
    return $items
}

function Assert-NoForbiddenMark {
    param([string]$Body)
    foreach ($name in $NeverNames) {
        if ($Body.IndexOf($name, [StringComparison]::Ordinal) -ge 0) {
            throw ('生成结果含禁用文件名：{0}' -f $name)
        }
    }
}

function ConvertTo-XmlText {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $Text.ToCharArray()) {
        $code = [int]$ch
        if ($code -eq 9 -or $code -eq 10 -or $code -eq 13 -or $code -ge 32) {
            [void]$sb.Append($ch)
        }
    }
    return $sb.ToString().Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
}

function Get-WordParagraph {
    param([string]$Style, [string]$Text)
    $safe = ConvertTo-XmlText $Text
    if ([string]::IsNullOrEmpty($Style)) {
        return '<w:p><w:r><w:t xml:space="preserve">' + $safe + '</w:t></w:r></w:p>'
    }
    return '<w:p><w:pPr><w:pStyle w:val="' + $Style + '"/></w:pPr><w:r><w:t xml:space="preserve">' + $safe + '</w:t></w:r></w:p>'
}

function Add-ZipText {
    param($Zip, [string]$Name, [string]$Text)
    $entry = $Zip.CreateEntry($Name, [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally {
        $stream.Close()
    }
}

function Write-QaDocx {
    param($Items, [int]$Version, [string]$UpdatedAt, [string]$Path)
    $paras = New-Object System.Text.StringBuilder
    [void]$paras.Append((Get-WordParagraph -Style 'Heading1' -Text '企微客户问答库'))
    [void]$paras.Append((Get-WordParagraph -Style '' -Text ('版本 {0}，更新于 {1}，共 {2} 条。' -f $Version, $UpdatedAt, @($Items).Count)))
    foreach ($item in $Items) {
        [void]$paras.Append((Get-WordParagraph -Style 'Heading2' -Text ('问：' + $item.Question)))
        $answerLines = ($item.Answer -replace "`r`n", "`n" -replace "`r", "`n") -split "`n", -1
        $wrote = $false
        foreach ($answerLine in $answerLines) {
            if ([string]::IsNullOrWhiteSpace($answerLine)) { continue }
            [void]$paras.Append((Get-WordParagraph -Style '' -Text $answerLine))
            $wrote = $true
        }
        if (-not $wrote) {
            throw ('docx 有一条空答案：{0}' -f $item.Question)
        }
    }
    $documentXml = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
'@ + $paras.ToString() + @'
    <w:sectPr>
      <w:pgSz w:w="11906" w:h="16838"/>
      <w:pgMar w:top="1134" w:right="1134" w:bottom="1134" w:left="1134"/>
    </w:sectPr>
  </w:body>
</w:document>
'@
    $contentTypes = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
</Types>
'@
    $rels = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>
'@
    $docRels = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
'@
    $styles = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:docDefaults>
    <w:rPrDefault>
      <w:rPr>
        <w:rFonts w:ascii="微软雅黑" w:hAnsi="微软雅黑" w:eastAsia="微软雅黑"/>
        <w:sz w:val="21"/>
        <w:szCs w:val="21"/>
      </w:rPr>
    </w:rPrDefault>
  </w:docDefaults>
  <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
    <w:name w:val="Normal"/>
    <w:qFormat/>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading1">
    <w:name w:val="heading 1"/>
    <w:basedOn w:val="Normal"/>
    <w:qFormat/>
    <w:pPr><w:outlineLvl w:val="0"/></w:pPr>
    <w:rPr><w:b/><w:sz w:val="32"/><w:szCs w:val="32"/></w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading2">
    <w:name w:val="heading 2"/>
    <w:basedOn w:val="Normal"/>
    <w:qFormat/>
    <w:pPr><w:outlineLvl w:val="1"/><w:spacing w:before="240" w:after="60"/></w:pPr>
    <w:rPr><w:b/><w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr>
  </w:style>
</w:styles>
'@
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $dir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }
    $tmp = $Path + '.tmp'
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($tmp, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Add-ZipText -Zip $zip -Name '[Content_Types].xml' -Text $contentTypes
        Add-ZipText -Zip $zip -Name '_rels/.rels' -Text $rels
        Add-ZipText -Zip $zip -Name 'word/document.xml' -Text $documentXml
        Add-ZipText -Zip $zip -Name 'word/_rels/document.xml.rels' -Text $docRels
        Add-ZipText -Zip $zip -Name 'word/styles.xml' -Text $styles
    }
    finally {
        $zip.Dispose()
    }
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    Move-Item -LiteralPath $tmp -Destination $Path
}

function Get-QaBody {
    param($Items)
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($item in $Items) {
        $lines.Add('问：' + $item.Question)
        $answerLines = ($item.Answer -replace "`r`n", "`n" -replace "`r", "`n") -split "`n", -1
        foreach ($answerLine in $answerLines) {
            if ([string]::IsNullOrWhiteSpace($answerLine)) { continue }
            $lines.Add($answerLine)
        }
    }
    return ($lines -join "`n")
}

function Read-DocxSnapshot {
    param([string]$Path)
    $snap = @{ Version = 0; UpdatedAt = ''; Body = '' }
    if (-not (Test-Path -LiteralPath $Path)) { return $snap }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.GetEntry('word/document.xml')
        if ($null -eq $entry) {
            throw ('已有 docx 缺少 word/document.xml：{0}' -f $Path)
        }
        $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
        try { $xml = $reader.ReadToEnd() } finally { $reader.Close() }
    }
    finally {
        $zip.Dispose()
    }
    $texts = [regex]::Matches($xml, '<w:t[^>]*>(.*?)</w:t>')
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($m in $texts) {
        $t = $m.Groups[1].Value.Replace('&amp;', '&').Replace('&lt;', '<').Replace('&gt;', '>')
        $lines.Add($t)
    }
    if ($lines.Count -lt 2) {
        throw ('已有 docx 读不出标题和版本：{0}' -f $Path)
    }
    $ver = [regex]::Match($lines[1], '版本\s+(\d+)，更新于\s+(\d{4}-\d{2}-\d{2})')
    if (-not $ver.Success) {
        throw ('已有 docx 版本行无法识别：{0}' -f $lines[1])
    }
    $snap.Version = [int]$ver.Groups[1].Value
    $snap.UpdatedAt = $ver.Groups[2].Value
    if ($lines.Count -gt 2) {
        $body = New-Object System.Collections.Generic.List[string]
        for ($i = 2; $i -lt $lines.Count; $i++) { $body.Add($lines[$i]) }
        $snap.Body = ($body -join "`n")
    }
    return $snap
}

function Register-DailyTask {
    $tr = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    & schtasks.exe /Create /TN 'WorkshopMes-WeComQa' /SC DAILY /ST 08:00 /F /TR $tr
    if ($LASTEXITCODE -ne 0) {
        throw ('注册计划任务失败，schtasks 退出码 {0}' -f $LASTEXITCODE)
    }
    Write-Log -Level 'INFO' -File '-' -Step '计划任务' -Message '已注册 WorkshopMes-WeComQa，每天 08:00'
}

foreach ($name in $NeverNames) {
    foreach ($row in $Whitelist) {
        if ($row.File -eq $name) {
            throw ('白名单误含禁用文件：{0}' -f $name)
        }
    }
}

if ($RegisterTask) {
    Register-DailyTask
}

Write-Log -Level 'INFO' -File '-' -Step '开始' -Message ('SupportDocs={0} 输出={1}' -f $SupportDocsRoot, $DocxPath)

$ord = 0
$parsed = New-Object System.Collections.Generic.List[object]
$okFiles = 0
$failFiles = 0

foreach ($row in $Whitelist) {
    $path = Join-Path $SupportDocsRoot $row.File
    if (-not (Test-Path -LiteralPath $path)) {
        $failFiles = $failFiles + 1
        Write-Log -Level 'ERROR' -File $row.File -Step '读取' -Message ('文件不存在：{0}' -f $path)
        continue
    }
    try {
        $text = Read-Utf8Text -Path $path
        $batch = $null
        if ($row.Kind -eq 'num') {
            $batch = Parse-Numbered -Text $text -Source $row.File -Priority $row.Priority -Ord ([ref]$ord)
        }
        elseif ($row.Kind -eq 'ask') {
            $batch = Parse-Ask -Text $text -Source $row.File -Priority $row.Priority -Ord ([ref]$ord)
        }
        elseif ($row.Kind -eq 'q') {
            $batch = Parse-QHeading -Text $text -Source $row.File -Priority $row.Priority -Ord ([ref]$ord)
        }
        else {
            throw ('未知解析类型：{0}' -f $row.Kind)
        }
        foreach ($one in @($batch)) {
            if ($null -ne $one) { $parsed.Add($one) }
        }
        $okFiles = $okFiles + 1
        Write-Log -Level 'INFO' -File $row.File -Step '解析' -Message ('抽出 {0} 条' -f @($batch).Count)
    }
    catch {
        $failFiles = $failFiles + 1
        Write-Log -Level 'ERROR' -File $row.File -Step '读取或解析' -Message $_.Exception.Message
    }
}

if ($okFiles -eq 0) {
    Write-Log -Level 'ERROR' -File '-' -Step '结束' -Message '三份白名单都失败，未写问答库'
    exit 1
}

$map = @{}
foreach ($item in $parsed) {
    if ([string]::IsNullOrWhiteSpace($item.Key) -or [string]::IsNullOrWhiteSpace($item.Source)) {
        Write-Log -Level 'WARN' -File $item.Source -Step '去重' -Message '空问句或无来源，已丢弃'
        continue
    }
    if ($map.ContainsKey($item.Key)) {
        $old = $map[$item.Key]
        if ($item.Priority -gt $old.Priority) {
            Write-Log -Level 'INFO' -File $item.Source -Step '去重' -Message ('同题保留 {0}，丢弃 {1}：{2}' -f $item.Source, $old.Source, $item.Question)
            $map[$item.Key] = $item
        }
        else {
            Write-Log -Level 'INFO' -File $old.Source -Step '去重' -Message ('同题保留 {0}，丢弃 {1}：{2}' -f $old.Source, $item.Source, $old.Question)
        }
    }
    else {
        $map[$item.Key] = $item
    }
}

$kept = @($map.Values) | Sort-Object @{ Expression = 'Priority'; Descending = $true }, @{ Expression = 'Ord'; Descending = $false }
$today = Get-Date -Format 'yyyy-MM-dd'
$qaBody = Get-QaBody -Items $kept
Assert-NoForbiddenMark -Body $qaBody
$existing = Read-DocxSnapshot -Path $DocxPath

$version = 1
$updatedAt = $today
if ($existing.Version -ge 1 -and $existing.Body -eq $qaBody) {
    $version = $existing.Version
    if (-not [string]::IsNullOrWhiteSpace($existing.UpdatedAt)) {
        $updatedAt = $existing.UpdatedAt
    }
    Write-Log -Level 'INFO' -File '-' -Step '版本' -Message ('内容未变，version 保持 {0}，updatedAt 保持 {1}' -f $version, $updatedAt)
}
elseif ($existing.Version -ge 1) {
    $version = $existing.Version + 1
    Write-Log -Level 'INFO' -File '-' -Step '版本' -Message ('内容有变化，version {0} -> {1}，updatedAt={2}' -f $existing.Version, $version, $updatedAt)
}
else {
    Write-Log -Level 'INFO' -File '-' -Step '版本' -Message ('首次生成，version={0}，updatedAt={1}' -f $version, $updatedAt)
}

Write-QaDocx -Items $kept -Version $version -UpdatedAt $updatedAt -Path $DocxPath
Write-Log -Level 'INFO' -File '-' -Step '结束' -Message ('写入 {0} 条，失败源文件 {1} 个，version={2}，docx={3}' -f @($kept).Count, $failFiles, $version, $DocxPath)
exit 0
