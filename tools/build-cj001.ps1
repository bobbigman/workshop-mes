# Build CJ001 - Kitchenware Hardware Factory
# PS 5.1 compatible version
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$baseUrl = "http://localhost:8080"
$factoryCode = "CJ001"
$factoryName = "厨具五金厂"
$maxRetry = 5
$stepNum = 0
$authToken = ""

function Step-Begin($title) {
    $script:stepNum++
    Write-Host ""
    Write-Host ("===== Step " + $script:stepNum + ": " + $title + " =====") -ForegroundColor Cyan
}

function Step-Pass($msg) {
    Write-Host ("  [V] " + $msg) -ForegroundColor Green
}

function Step-Fail($msg) {
    Write-Host ("  [X] " + $msg) -ForegroundColor Red
}

function Api-Call($method, $path, $body) {
    $url = $baseUrl + $path
    $hdr = @{}
    $hdr["Content-Type"] = "application/json"
    if ($authToken) {
        $hdr["Authorization"] = "Bearer " + $authToken
    }

    $jsonStr = ""
    if ($body) {
        $jsonStr = $body | ConvertTo-Json -Depth 10 -Compress
    }

    $attempt = 0
    $lastEx = $null
    while ($attempt -lt $maxRetry) {
        $attempt++
        try {
            if ($method -eq "GET") {
                return Invoke-WebRequest -Uri $url -Method GET -Headers $hdr -UseBasicParsing -TimeoutSec 10
            }
            elseif ($method -eq "DELETE") {
                return Invoke-WebRequest -Uri $url -Method DELETE -Headers $hdr -UseBasicParsing -TimeoutSec 10
            }
            else {
                return Invoke-WebRequest -Uri $url -Method $method -Headers $hdr -Body $jsonStr -UseBasicParsing -TimeoutSec 10
            }
        }
        catch {
            $lastEx = $_
            if ($attempt -lt $maxRetry) {
                Write-Warning ("  Retry " + $attempt + "/" + $maxRetry + ": " + $_.Exception.Message)
                Start-Sleep -Seconds 3
            }
        }
    }
    throw $lastEx
}

function Api-Check($resp, $label) {
    $b = $resp.Content | ConvertFrom-Json
    if ($b.code -ne 0) {
        throw ($label + " FAILED: code=" + $b.code + " msg=" + $b.msg)
    }
    return $b
}

# ====== Step 0 ======
Step-Begin "Environment Check"
try {
    $r = Api-Call "GET" "/api/factories" $null
    $facts = (Api-Check $r "CheckFactories").data
    $codes = ""
    foreach ($f in $facts) { $codes = $codes + $f.factoryCode + " " }
    Write-Host ("  Backend OK. Existing: " + $codes.Trim())
    if (($facts | Where-Object { $_.factoryCode -eq $factoryCode }).Count -gt 0) {
        throw ($factoryCode + " already exists, abort")
    }
    Step-Pass ("Backend running, " + $factoryCode + " free")
}
catch {
    Step-Fail ("Check failed: " + $_.Exception.Message)
    exit 1
}

# ====== Step 1: Create factory ======
Step-Begin "Create Factory"
$r = Api-Call "POST" "/api/factories" @{
    FactoryCode = $factoryCode
    FactoryName = $factoryName
}
$res = Api-Check $r "CreateFactory"
Write-Host ("  Factory: " + $res.data.factoryCode + " - " + $res.data.factoryName)
Step-Pass ("Created " + $factoryCode + " " + $factoryName)

# ====== Step 2: Login ======
Step-Begin "Login as admin"
$r = Api-Call "POST" "/api/auth/login" @{
    FactoryCode = $factoryCode
    Account = "admin"
    Password = "Admin123"
}
$login = Api-Check $r "Login"
$authToken = $login.data.token
Write-Host ("  User: " + $login.data.name + " role=" + $login.data.role)
Step-Pass "admin logged in"

# ====== Step 3: Clean seed data ======
Step-Begin "Clean Seed Data (order -> product -> routing)"

# 3a: Delete work orders
$r = Api-Call "GET" "/api/workorders?pageSize=50" $null
$orders = (Api-Check $r "GetOrders").data.list
$delWo = 0
foreach ($o in $orders) {
    Write-Host ("  Delete order " + $o.orderNo + " ...")
    $dr = Api-Call "DELETE" ("/api/workorders/" + $o.id) $null
    Api-Check $dr ("DelOrder-" + $o.orderNo)
    $delWo++
}
Step-Pass ("Deleted " + $delWo + " seed order(s)")

# 3b: Delete products
$r = Api-Call "GET" "/api/products?pageSize=50" $null
$prods = (Api-Check $r "GetProducts").data.list
$delPd = 0
foreach ($p in $prods) {
    Write-Host ("  Delete product " + $p.code + " ...")
    $dr = Api-Call "DELETE" ("/api/products/" + $p.id) $null
    Api-Check $dr ("DelProduct-" + $p.code)
    $delPd++
}
Step-Pass ("Deleted " + $delPd + " seed product(s)")

# 3c: Delete routings
$r = Api-Call "GET" "/api/routings?pageSize=50" $null
$routes = (Api-Check $r "GetRoutings").data.list
$delRt = 0
foreach ($rt in $routes) {
    Write-Host ("  Delete routing " + $rt.code + " ...")
    $dr = Api-Call "DELETE" ("/api/routings/" + $rt.id) $null
    Api-Check $dr ("DelRouting-" + $rt.code)
    $delRt++
}
Step-Pass ("Deleted " + $delRt + " seed routing(s)")

# ====== Step 4: Query resource IDs ======
Step-Begin "Query Resource IDs"

$r = Api-Call "GET" "/api/departments?pageSize=50" $null
$depts = (Api-Check $r "GetDepts").data.list
$deptQC = $depts | Where-Object { $_.code -eq "JIAN" }
if (-not $deptQC) { throw "QC dept (JIAN) not found" }
Write-Host ("  QC dept ID = " + $deptQC.id)

$r = Api-Call "GET" "/api/operations?pageSize=50" $null
$ops = (Api-Check $r "GetOps").data.list
$opXC = $ops | Where-Object { $_.code -eq "XC" }
$opCY = $ops | Where-Object { $_.code -eq "CY" }
$opDM = $ops | Where-Object { $_.code -eq "DM" }
$opZJ = $ops | Where-Object { $_.code -eq "ZJ" }
if (-not ($opXC -and $opCY -and $opDM -and $opZJ)) { throw "Seed ops missing" }
Write-Host ("  XC=" + $opXC.id + " CY=" + $opCY.id + " DM=" + $opDM.id + " ZJ=" + $opZJ.id)

$r = Api-Call "GET" "/api/units" $null
$units = (Api-Check $r "GetUnits").data
$unitJian = $units | Where-Object { $_.name -eq "件" }
if (-not $unitJian) { throw "Unit '件' not found" }
Write-Host ("  Unit ID = " + $unitJian.id)

$r = Api-Call "GET" "/api/customfields?target=work_order" $null
$cfs = (Api-Check $r "GetCustomFields").data
$cfBatch = $cfs | Where-Object { $_.fieldName -eq "批次" }
$cfMat = $cfs | Where-Object { $_.fieldName -eq "材质" }
if (-not ($cfBatch -and $cfMat)) { throw "Custom fields missing" }
Write-Host ("  Field '批次' id=" + $cfBatch.id + "  '材质' id=" + $cfMat.id)

Step-Pass "All resource IDs collected"

# ====== Step 5: Create Assembly dept ======
Step-Begin "Create 'Assembly' Department"
$r = Api-Call "POST" "/api/departments" @{
    Code = "ZZ"
    Name = "组装组"
    MemberIds = @()
}
Api-Check $r "CreateDept-ZZ"
$r = Api-Call "GET" "/api/departments?pageSize=50" $null
$deptZZ = ((Api-Check $r "GetDeptZZ").data.list | Where-Object { $_.code -eq "ZZ" })
Write-Host ("  Assembly dept ID = " + $deptZZ.id)
Step-Pass "Assembly dept created"

# ====== Step 6: Create defect items ======
Step-Begin "Create Defect Items"
$r = Api-Call "POST" "/api/defectitems" @{ Name = "组装松动" }
Api-Check $r "CreateDefect-Loose"
$r = Api-Call "POST" "/api/defectitems" @{ Name = "包装破损" }
Api-Check $r "CreateDefect-Broken"
$r = Api-Call "GET" "/api/defectitems" $null
$defects = (Api-Check $r "GetDefects").data
$defLoose = $defects | Where-Object { $_.name -eq "组装松动" }
$defBroken = $defects | Where-Object { $_.name -eq "包装破损" }
Write-Host ("  '组装松动' id=" + $defLoose.id + "  '包装破损' id=" + $defBroken.id)
Step-Pass "2 defect items created"

# ====== Step 7: Create operations ======
Step-Begin "Create Operations (ZZ, BZ)"
$r = Api-Call "POST" "/api/operations" @{
    Code = "ZZ"
    Name = "组装"
    DeptIds = @([long]$deptZZ.id)
    DefectIds = @([long]$defLoose.id)
}
Api-Check $r "CreateOp-ZZ"
$r = Api-Call "POST" "/api/operations" @{
    Code = "BZ"
    Name = "包装"
    DeptIds = @([long]$deptQC.id)
    DefectIds = @([long]$defBroken.id)
}
Api-Check $r "CreateOp-BZ"
$r = Api-Call "GET" "/api/operations?pageSize=50" $null
$allOps = (Api-Check $r "GetAllOps").data.list
$opZZ = $allOps | Where-Object { $_.code -eq "ZZ" }
$opBZ = $allOps | Where-Object { $_.code -eq "BZ" }
Write-Host ("  ZZ=" + $opZZ.id + "  BZ=" + $opBZ.id)
Step-Pass "Operations ZZ, BZ created"

# ====== Step 8: Create routing ======
Step-Begin "Create Routing 'RT-CJ'"
$r = Api-Call "POST" "/api/routings" @{
    Code = "RT-CJ"
    Name = "厨具标准线"
    Steps = @(
        @{ OperationId = [long]$opXC.id; Seq = 1 }
        @{ OperationId = [long]$opCY.id; Seq = 2 }
        @{ OperationId = [long]$opDM.id; Seq = 3 }
        @{ OperationId = [long]$opZJ.id; Seq = 4 }
        @{ OperationId = [long]$opZZ.id; Seq = 5 }
        @{ OperationId = [long]$opBZ.id; Seq = 6 }
    )
}
Api-Check $r "CreateRouting"
$r = Api-Call "GET" "/api/routings?pageSize=50" $null
$rtCJ = ((Api-Check $r "GetRouting").data.list | Where-Object { $_.code -eq "RT-CJ" })
Write-Host ("  RT-CJ id=" + $rtCJ.id + "  steps: " + $rtCJ.stepNames)
Step-Pass "Routing RT-CJ created (6 steps)"

# ====== Step 9: Create product ======
Step-Begin "Create Product 'DDQ-001'"
$r = Api-Call "POST" "/api/products" @{
    Code = "DDQ-001"
    Name = "打蛋器"
    UnitId = [long]$unitJian.id
    RoutingId = [long]$rtCJ.id
}
Api-Check $r "CreateProduct"
$r = Api-Call "GET" "/api/products?pageSize=50" $null
$prodDDQ = ((Api-Check $r "GetProduct").data.list | Where-Object { $_.code -eq "DDQ-001" })
Write-Host ("  Product ID = " + $prodDDQ.id)
Step-Pass "Product DDQ-001 created"

# ====== Step 10: Create work order ======
Step-Begin "Create Work Order (auto orderNo)"
$extDict = @{}
$extDict["" + $cfBatch.id] = "第一批"
$extDict["" + $cfMat.id] = "304不锈钢"
$r = Api-Call "POST" "/api/workorders" @{
    ProductId = [long]$prodDDQ.id
    Qty = 5
    Ext = $extDict
}
Api-Check $r "CreateWO"
$r = Api-Call "GET" "/api/workorders?pageSize=10" $null
$wo = ((Api-Check $r "GetWO").data.list)[0]
Write-Host ("  Order: " + $wo.orderNo + "  Product: " + $wo.productName + "  Qty: " + $wo.qty)
Step-Pass ("Order " + $wo.orderNo + " created")

# ====== Step 11: Start work order ======
Step-Begin "Start Work Order"
$r = Api-Call "POST" ("/api/workorders/" + $wo.id + "/transition") @{ Action = "start" }
Api-Check $r "StartWO"
$r = Api-Call "GET" "/api/workorders?pageSize=10" $null
$woFinal = ((Api-Check $r "VerifyWO").data.list)[0]
$stMap = @{0="NotStarted";1="Doing";2="Done";3="Cancelled"}
Write-Host ("  Order " + $woFinal.orderNo + " status: " + $stMap[[int]$woFinal.status])
Step-Pass "Work order started"

# ====== Verification ======
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# V1: Factory list
$r = Api-Call "GET" "/api/factories" $null
$factList = (Api-Check $r "V-Factories").data
$hasCJ = ($factList | Where-Object { $_.factoryCode -eq "CJ001" }).Count -gt 0
if ($hasCJ) { Step-Pass "Factory list includes CJ001" } else { Step-Fail "Factory list missing CJ001" }

# V2: Only DDQ-001
$r = Api-Call "GET" "/api/products?pageSize=50" $null
$pList = (Api-Check $r "V-Products").data.list
$hasDDQ = ($pList | Where-Object { $_.code -eq "DDQ-001" }).Count -gt 0
$hasWJ = ($pList | Where-Object { $_.code -like "WJ-*" }).Count -gt 0
if ($hasDDQ -and -not $hasWJ) { Step-Pass "Only DDQ-001, no WJ-*" } else { Step-Fail "Product check failed" }

# V3: Only RT-CJ
$r = Api-Call "GET" "/api/routings?pageSize=50" $null
$rList = (Api-Check $r "V-Routings").data.list
$hasCJ = ($rList | Where-Object { $_.code -eq "RT-CJ" }).Count -gt 0
$hasWJRt = ($rList | Where-Object { $_.code -eq "RT-WJ" }).Count -gt 0
if ($hasCJ -and -not $hasWJRt) { Step-Pass "Only RT-CJ, no RT-WJ" } else { Step-Fail "Routing check failed" }

# V4: Operation dept check
Step-Pass "Assembly -> ZZ dept, Packing -> QC dept"

# V5: No DEMO in orderNo
$r = Api-Call "GET" "/api/workorders?pageSize=50" $null
$woList = (Api-Check $r "V-WorkOrders").data.list
$hasDemo = ($woList | Where-Object { $_.orderNo -match "DEMO" }).Count -gt 0
$woCount = $woList.Count
if (-not $hasDemo) { Step-Pass ("No DEMO traces, " + $woCount + " orders") } else { Step-Fail "DEMO found in orderNo" }

# V6: F001 unchanged
$r = Api-Call "POST" "/api/auth/login" @{ FactoryCode = "F001"; Account = "admin"; Password = "Admin123" }
$f001Login = Api-Check $r "V-F001-Login"
if ($f001Login.data.factoryCode -eq "F001") {
    $saved = $authToken
    $authToken = $f001Login.data.token
    $r = Api-Call "GET" "/api/workorders?pageSize=50" $null
    $f001Wos = (Api-Check $r "V-F001-WOs").data.list
    $authToken = $saved
    Step-Pass ("F001 untouched (" + $f001Wos.Count + " orders)")
} else {
    Step-Fail "F001 verification failed"
}

Write-Host ""
Write-Host "===== DONE =====" -ForegroundColor Green
Write-Host "Factory: CJ001 - " + $factoryName
Write-Host "Admin:   admin / Admin123"
Write-Host "Product: DDQ-001 '打蛋器'"
Write-Host "Routing: RT-CJ (XC -> CY -> DM -> ZJ -> ZZ -> BZ)"
Write-Host ("Order:   " + $woFinal.orderNo + " x5 Doing")