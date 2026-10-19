# 从 SupportDocs 里给客户看的手册生成 docs/企微客户问答库.docx
# Windows PowerShell 5.1。不读指令目录，不改源文件，不进 MES。
# 默认收入目录下每份客户手册：问答稿按原格式抽，操作手册按章节抽，能力表按行抽。
# 新功能写进这些手册或能力表即可，不用另维护「必出现」清单。
# 不进库的只有名单里的内部文件（配置暗语、字段名、销售底稿、应答规则）。
# 产物按文件名加版本：企微客户问答库V1.docx、V2.docx … 内容有变化才出下一号，最高 V9999999。旧号不覆盖。
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
    'README.md',
    '_retrieval.json'
)

$MaxVersion = 9999999

$SkipHeadingMarks = @(
    'AI 兜底',
    '后端接口',
    '前端入口',
    '维护日志',
    '接头',
    '铁律',
    '给 AI 助手'
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

function Get-ParseKind {
    param([string]$Text)
    if ($Text -match '(?m)^##\s*二、高情商问答' -and $Text -match '(?m)^###\s+Q\d+\s+') { return 'q' }
    if ([regex]::IsMatch($Text, '(?m)^\*\*问[:：]')) { return 'ask' }
    if ([regex]::IsMatch($Text, '(?m)^###\s+\d+\.\s+')) { return 'num' }
    if ([regex]::IsMatch($Text, '(?m)^##\s+\S')) { return 'manual' }
    return ''
}

function Get-KindPriority {
    param([string]$Kind)
    switch ($Kind) {
        'num' { return 40 }
        'ask' { return 30 }
        'q' { return 20 }
        'manual' { return 12 }
        default { return 10 }
    }
}

function Remove-FrontMatter {
    param([string]$Text)
    if ($Text.StartsWith('---')) {
        $idx = $Text.IndexOf("`n---")
        if ($idx -gt 0) {
            return $Text.Substring($idx + 4).TrimStart()
        }
    }
    return $Text
}

function Test-SkipHeading {
    param([string]$Heading)
    foreach ($mark in $SkipHeadingMarks) {
        if ($Heading.IndexOf($mark, [StringComparison]::Ordinal) -ge 0) { return $true }
    }
    return $false
}

function Repair-CustomerBody {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    $s = $Text -replace "`r`n", "`n" -replace "`r", "`n"
    $s = $s -replace '(?m)^!\[[^\]]*\]\([^)]+\)\s*$', ''
    $s = $s -replace '!\[[^\]]*\]\([^)]+\)', ''
    $s = $s -replace '\[([^\]]+)\]\([^)]+\)', '$1'
    $s = $s -replace '`[^`]*`', ''
    $s = $s -replace '（docs/[^）]+）', ''
    $s = $s -replace 'docs/[0-9A-Za-z]+[、，,]?\s*', ''
    $s = $s -replace '（role=\d+）', ''
    $s = $s -replace '（[A-Za-z0-9_ ./+\-]+）', ''
    $s = $s -replace '✅|🚧|⚠️', ''
    $s = $s -replace '问「[^」]{0,40}」时[，,][^。\n]*[。]?', ''
    $s = $s -replace '问怎么用时[^。\n]*[。]?', ''
    $s = $s -replace '念下面[^。\n]*[。]?', ''
    $s = $s -replace '不要说没有[^。\n]*[。]?', ''
    $s = $s -replace '不要答成[^。\n]*[。]?', ''
    $lines = $s -split "`n", -1
    $kept = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        $line = $line -replace '^#{1,6}\s+', ''
        $line = $line -replace '^\s*>\s*', ''
        $line = $line -replace '#(?=\S)', ''
        if ($line -match '只念下面|不要说知识库|不要自己编|禁止编|供\s*AI|别答成|Controller|/api/|licenseTier|LicenseTier|不要教客户') { continue }
        $kept.Add($line.TrimEnd())
    }
    return Format-CustomerText (($kept -join "`n"))
}

function Get-MdCells {
    param([string]$Line)
    $t = $Line.Trim().Trim('|')
    $cells = New-Object System.Collections.Generic.List[string]
    foreach ($part in ($t -split '\|')) { $cells.Add($part.Trim()) }
    return $cells
}

function Test-SepCells {
    param($Cells)
    if ($Cells.Count -eq 0) { return $false }
    foreach ($c in $Cells) {
        if ($c -notmatch '^:?-+:?$') { return $false }
    }
    return $true
}

function Get-SectionTables {
    param([string]$Body)
    $lines = $Body -replace "`r`n", "`n" -replace "`r", "`n" -split "`n", -1
    $tables = New-Object System.Collections.Generic.List[object]
    $i = 0
    while ($i -lt $lines.Length) {
        $trim = $lines[$i].Trim()
        $isRow = $trim.StartsWith('|') -and $trim.EndsWith('|') -and $trim.Length -gt 1
        if (-not $isRow) { $i++; continue }
        $start = $i
        $rows = New-Object System.Collections.Generic.List[object]
        while ($i -lt $lines.Length) {
            $trim = $lines[$i].Trim()
            $isRow = $trim.StartsWith('|') -and $trim.EndsWith('|') -and $trim.Length -gt 1
            if (-not $isRow) { break }
            $cells = @(Get-MdCells -Line $trim)
            if (-not (Test-SepCells -Cells $cells)) { $rows.Add($cells) }
            $i++
        }
        if ($rows.Count -ge 2) {
            $data = New-Object System.Collections.Generic.List[object]
            for ($r = 1; $r -lt $rows.Count; $r++) { $data.Add($rows[$r]) }
            $tables.Add([pscustomobject]@{ Start = $start; End = $i; Headers = @($rows[0]); Data = $data })
        }
    }
    return $tables
}

function Get-KeptCells {
    param($Headers, $Row)
    $bits = New-Object System.Collections.Generic.List[string]
    for ($c = 0; $c -lt $Headers.Count; $c++) {
        $header = [string]$Headers[$c]
        if ($header -match '对应指令|详细文档|Controller|关键接口') { continue }
        $val = ''
        if ($c -lt $Row.Count) { $val = [string]$Row[$c] }
        $val = (Repair-CustomerBody $val)
        if ([string]::IsNullOrWhiteSpace($val) -or $val -eq '—' -or $val -eq '-') { continue }
        if ($c -eq 0) { $bits.Add($val) } else { $bits.Add($header + '：' + $val) }
    }
    return $bits
}

function Add-QaItem {
    param($Items, [string]$Question, [string]$Answer, [string]$Source, [int]$Priority, [ref]$Ord)
    $question = Repair-CustomerBody $Question
    $answer = Repair-CustomerBody $Answer
    if ([string]::IsNullOrWhiteSpace($question) -or [string]::IsNullOrWhiteSpace($answer)) { return }
    if ($answer -match '不重复展开|口径变更只改|统一以') { return }
    if ($question -match 'MCP|诊断') { return }
    $Ord.Value = $Ord.Value + 1
    $Items.Add([pscustomobject]@{
        Question = $question
        Answer   = $answer
        Source   = $Source
        Priority = $Priority
        Ord      = $Ord.Value
        Key      = (Get-QuestionKey $question)
    })
}

function Add-ManualItems {
    param([string]$Text, [string]$Source, [int]$Priority, [ref]$Ord, $Items)
    $bodyText = Remove-FrontMatter $Text
    $found = [regex]::Matches($bodyText, '(?m)^##\s+(.+?)\s*$')
    if ($found.Count -eq 0) { return }
    for ($i = 0; $i -lt $found.Count; $i++) {
        $heading = $found[$i].Groups[1].Value.Trim()
        if (Test-SkipHeading $heading) { continue }
        $start = $found[$i].Index + $found[$i].Length
        $end = $bodyText.Length
        if ($i + 1 -lt $found.Count) { $end = $found[$i + 1].Index }
        $chunk = $bodyText.Substring($start, $end - $start)
        $tables = @(Get-SectionTables -Body $chunk)
        $exploded = $false
        foreach ($table in $tables) {
            $head0 = ''
            if ($table.Headers.Count -gt 0) { $head0 = [string]$table.Headers[0] }
            if ($head0 -ne '能力' -and $head0 -ne '新功能' -and $head0 -ne '项') { continue }
            $exploded = $true
            $rowPriority = 8
            if ($head0 -eq '新功能') { $rowPriority = 9 }
            if ($head0 -eq '项') { $rowPriority = 7 }
            foreach ($row in $table.Data) {
                $name = ''
                if ($row.Count -gt 0) { $name = (Repair-CustomerBody ([string]$row[0])) }
                if ([string]::IsNullOrWhiteSpace($name)) { continue }
                $bits = @(Get-KeptCells -Headers $table.Headers -Row $row)
                $answerBits = New-Object System.Collections.Generic.List[string]
                foreach ($bit in $bits) {
                    if ($bit -eq $name) { continue }
                    $answerBits.Add($bit)
                }
                $answer = ($answerBits -join '。')
                if ($head0 -eq '新功能' -and $table.Headers.Count -ge 2 -and $row.Count -ge 2) {
                    $oneLine = Repair-CustomerBody ([string]$row[1])
                    if (-not [string]::IsNullOrWhiteSpace($oneLine)) { $answer = $oneLine }
                }
                if ($head0 -eq '项' -and $answer -match '未做|不做|未实现|占位|别承诺') {
                    $answer = '还没有。' + $answer
                }
                $q = '有没有' + $name + '？'
                if ($head0 -eq '项') { $q = '有没有' + $name + '？做了吗？' }
                Add-QaItem -Items $Items -Question $q -Answer $answer -Source $Source -Priority $rowPriority -Ord $Ord
            }
        }
        if ($exploded) { continue }
        $flat = $chunk
        foreach ($table in $tables) {
            $lines = Convert-TableBlockToText -Table $table
            $rawLines = ($chunk -replace "`r`n", "`n" -replace "`r", "`n") -split "`n", -1
            $rebuilt = New-Object System.Collections.Generic.List[string]
            for ($n = 0; $n -lt $rawLines.Length; $n++) {
                if ($n -eq $table.Start) {
                    foreach ($line in $lines) { $rebuilt.Add($line) }
                }
                if ($n -ge $table.Start -and $n -lt $table.End) { continue }
                $rebuilt.Add($rawLines[$n])
            }
            $flat = ($rebuilt -join "`n")
        }
        Add-QaItem -Items $Items -Question $heading -Answer $flat -Source $Source -Priority $Priority -Ord $Ord
    }
}

function Convert-TableBlockToText {
    param($Table)
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($row in $Table.Data) {
        $bits = @(Get-KeptCells -Headers $Table.Headers -Row $row)
        if ($bits.Count -gt 0) { $lines.Add(($bits -join '。')) }
    }
    return $lines
}

function Add-RollupItems {
    param($Items, [ref]$Ord)
    $featureLines = New-Object System.Collections.Generic.List[string]
    $wayLines = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    $copy = New-Object object[] $Items.Count
    for ($i = 0; $i -lt $Items.Count; $i++) { $copy[$i] = $Items[$i] }
    $ranked = $copy | Sort-Object Priority -Descending
    foreach ($item in $ranked) {
        if ($item.Source -ne '00-系统能力总地图.md') { continue }
        if ($item.Question -notmatch '^有没有([^？]+)？$') { continue }
        $name = $Matches[1]
        if ($seen.ContainsKey($name)) { continue }
        $seen[$name] = $true
        $first = ''
        foreach ($line in ($item.Answer -split "`n")) {
            if (-not [string]::IsNullOrWhiteSpace($line)) { $first = $line.Trim(); break }
        }
        if ([string]::IsNullOrWhiteSpace($first)) { $first = '见手册说明' }
        $featureLines.Add('- ' + $name + '：' + $first)
        $isWay = $name -match '报工|扫码|代报|补报'
        $notWay = $name -match '设置|报表|复核|改数|工资|看板|监控|异常|桌面|校验|留痕|修改'
        if ($isWay -and -not $notWay) {
            $detail = ''
            foreach ($manual in $copy) {
                if ($manual.Priority -lt 12) { continue }
                $q = [string]$manual.Question
                $hit = ($q -eq $name) -or $q.StartsWith($name + '（') -or $q.StartsWith($name + '：')
                if (-not $hit) { continue }
                foreach ($line in ($manual.Answer -split "`n")) {
                    if (-not [string]::IsNullOrWhiteSpace($line)) { $detail = $line.Trim(); break }
                }
                if (-not [string]::IsNullOrWhiteSpace($detail)) { break }
            }
            if ([string]::IsNullOrWhiteSpace($detail)) { $detail = $first }
            $wayLines.Add('- ' + $name + '：' + $detail)
        }
    }
    if ($featureLines.Count -gt 0) {
        $answer = "按当前能力表逐条列出，每条带入口和状态。状态写了未实现、未做、不做的，就回答还没有。`n" + ($featureLines -join "`n")
        Add-QaItem -Items $Items -Question '系统现在有哪些功能？分别在哪用？' -Answer $answer -Source '00-系统能力总地图.md' -Priority 28 -Ord $Ord
    }
    if ($wayLines.Count -gt 0) {
        $answer = "工人报工，按现在系统里已经有的入口，有这些：`n" + ($wayLines -join "`n") + "`n对话里口述数量、让机器人替工人填数提交，这项还没有。机器人只负责把人送进填数页，数还是工人自己填。"
        Add-QaItem -Items $Items -Question '员工报工有多少种方式？工人可以怎么报工？' -Answer $answer -Source '00-系统能力总地图.md' -Priority 28 -Ord $Ord
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

if (-not (Test-Path -LiteralPath $SupportDocsRoot)) {
    Write-Log -Level 'ERROR' -File '-' -Step '读取' -Message ('目录不存在：{0}' -f $SupportDocsRoot)
    exit 1
}

$files = @(Get-ChildItem -LiteralPath $SupportDocsRoot -Filter *.md -File | Sort-Object Name)
foreach ($file in $files) {
    $skip = $false
    foreach ($name in $NeverNames) {
        if ($file.Name -eq $name) { $skip = $true }
    }
    if ($skip) {
        Write-Log -Level 'INFO' -File $file.Name -Step '跳过' -Message '内部文件，不进客户问答库'
        continue
    }
    try {
        $text = Read-Utf8Text -Path $file.FullName
        $kind = Get-ParseKind -Text $text
        if ([string]::IsNullOrWhiteSpace($kind)) {
            Write-Log -Level 'WARN' -File $file.Name -Step '解析' -Message '没有问答或章节，已跳过'
            continue
        }
        $priority = Get-KindPriority -Kind $kind
        $before = $parsed.Count
        if ($kind -eq 'num') {
            foreach ($one in @(Parse-Numbered -Text $text -Source $file.Name -Priority $priority -Ord ([ref]$ord))) {
                if ($null -ne $one) { $parsed.Add($one) }
            }
        }
        elseif ($kind -eq 'ask') {
            foreach ($one in @(Parse-Ask -Text $text -Source $file.Name -Priority $priority -Ord ([ref]$ord))) {
                if ($null -ne $one) { $parsed.Add($one) }
            }
        }
        elseif ($kind -eq 'q') {
            foreach ($one in @(Parse-QHeading -Text $text -Source $file.Name -Priority $priority -Ord ([ref]$ord))) {
                if ($null -ne $one) { $parsed.Add($one) }
            }
        }
        else {
            Add-ManualItems -Text $text -Source $file.Name -Priority $priority -Ord ([ref]$ord) -Items $parsed
        }
        $okFiles = $okFiles + 1
        Write-Log -Level 'INFO' -File $file.Name -Step '解析' -Message ('抽出 {0} 条' -f ($parsed.Count - $before))
    }
    catch {
        $failFiles = $failFiles + 1
        Write-Log -Level 'ERROR' -File $file.Name -Step '读取或解析' -Message $_.Exception.Message
    }
}

if ($okFiles -eq 0) {
    Write-Log -Level 'ERROR' -File '-' -Step '结束' -Message '没有可用的客户手册，未写问答库'
    exit 1
}

Add-RollupItems -Items $parsed -Ord ([ref]$ord)

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

$docsDir = Split-Path -Parent $DocxPath
if ([string]::IsNullOrWhiteSpace($docsDir)) { $docsDir = $RepoRoot }
$maxNumber = 0
$latestPath = ''
if (Test-Path -LiteralPath $docsDir) {
    $named = @(Get-ChildItem -LiteralPath $docsDir -Filter '企微客户问答库V*.docx' -File)
    foreach ($file in $named) {
        $hit = [regex]::Match($file.Name, '^企微客户问答库V(\d+)\.docx$')
        if (-not $hit.Success) { continue }
        $n = 0
        if (-not [int]::TryParse($hit.Groups[1].Value, [ref]$n)) { continue }
        if ($n -gt $maxNumber) {
            $maxNumber = $n
            $latestPath = $file.FullName
        }
    }
}

$existing = @{ Version = 0; UpdatedAt = ''; Body = '' }
if (-not [string]::IsNullOrWhiteSpace($latestPath)) {
    $existing = Read-DocxSnapshot -Path $latestPath
}

$version = 1
$updatedAt = $today
$outPath = ''
if ($maxNumber -ge 1 -and $existing.Body -eq $qaBody) {
    $version = $maxNumber
    $outPath = $latestPath
    if (-not [string]::IsNullOrWhiteSpace($existing.UpdatedAt)) {
        $updatedAt = $existing.UpdatedAt
    }
    Write-Log -Level 'INFO' -File '-' -Step '版本' -Message ('内容未变，保持 V{0}，不另存新文件：{1}' -f $version, $outPath)
    Write-Log -Level 'INFO' -File '-' -Step '结束' -Message ('共 {0} 条，失败源文件 {1} 个，version={2}，docx={3}' -f @($kept).Count, $failFiles, $version, $outPath)
    exit 0
}

$version = $maxNumber + 1
if ($version -gt $MaxVersion) {
    throw ('版本已到 V{0}，不再生成新文件' -f $MaxVersion)
}
$outPath = Join-Path $docsDir ('企微客户问答库V{0}.docx' -f $version)
Write-Log -Level 'INFO' -File '-' -Step '版本' -Message ('内容有变化，另存 V{0}，updatedAt={1}' -f $version, $updatedAt)

Write-QaDocx -Items $kept -Version $version -UpdatedAt $updatedAt -Path $outPath
Write-Log -Level 'INFO' -File '-' -Step '结束' -Message ('写入 {0} 条，失败源文件 {1} 个，version={2}，docx={3}' -f @($kept).Count, $failFiles, $version, $outPath)
exit 0
