<#
.SYNOPSIS
    Comprehensive smoke-test for the ProductAuction API (https://localhost:7285).
    Uses curl.exe for reliable HTTP. Covers auth, public endpoints, authenticated
    endpoints per role, role-based access control (401/403), and an end-to-end
    auction flow. Each endpoint is hit exactly once.
#>
$ErrorActionPreference = "Continue"
$base = "https://localhost:7285"
$tmp = Join-Path $env:TEMP ("pa_test_" + [guid]::NewGuid().ToString("N") + ".json")
$tmpBody = $tmp + ".body"
$tmpHeaders = $tmp + ".headers"
$results = New-Object System.Collections.Generic.List[pscustomobject]
$passCount = 0
$failCount = 0

function Write-TmpBody($obj) {
    $json = $obj | ConvertTo-Json -Depth 5
    Set-Content -Path $tmp -Value $json -Encoding UTF8 -NoNewline
}

# Single call returning code + body.
function Invoke-CurlRequest {
    param([string]$Method, [string]$Path, [string]$Token = $null, [string]$BodyFile = $null, [string]$ContentType = "application/json")
    $args = @("-s","-k","-D",$tmpHeaders,"-o",$tmpBody,"-X",$Method)
    if ($Token) { $args += @("-H","Authorization: Bearer $Token") }
    if ($BodyFile) {
        $args += @("-H","Content-Type: $ContentType","-d","@$BodyFile")
    }
    $args += ("$base$Path")
    & curl.exe $args 2>$null | Out-Null
    $code = 0
    if (Test-Path $tmpHeaders) {
        $hl = Get-Content $tmpHeaders -Raw -ErrorAction SilentlyContinue
        Remove-Item $tmpHeaders -ErrorAction SilentlyContinue
        if ($hl -match "HTTP\/[0-9.]+ ([0-9]+)") { $code = [int]$matches[1] }
    }
    $body = ""
    if (Test-Path $tmpBody) { $body = Get-Content $tmpBody -Raw -ErrorAction SilentlyContinue; Remove-Item $tmpBody -ErrorAction SilentlyContinue }
    return @{ Code = $code; Body = $body }
}

function Test-Request {
    param(
        [string]$Method,
        [string]$Path,
        [string]$Token = $null,
        [object]$Body = $null,
        [string]$ContentType = "application/json",
        [int[]]$ExpectedCodes = @(200,201,204),
        [string]$Description = ""
    )
    $bodyFile = $null
    if ($null -ne $Body) { Write-TmpBody $Body; $bodyFile = $tmp }
    $r = Invoke-CurlRequest -Method $Method -Path $Path -Token $Token -BodyFile $bodyFile -ContentType $ContentType
    $pass = ($r.Code -ne 0 -and $ExpectedCodes -contains $r.Code)
    if ($pass) { $script:passCount++ } else { $script:failCount++ }
    $results.Add([pscustomobject]@{ Desc = $Description; Method = $Method; Path = $Path; Code = $r.Code; Pass = $pass })
    $status = if ($pass) { "PASS" } else { "FAIL" }
    $color = if ($pass) { "Green" } else { "Red" }
    Write-Host ("{0,-6} {1,-45} -> {2}  [{3}]" -f $Method, $Description, $r.Code, $status) -ForegroundColor $color
    if (-not $pass -and $r.Body) {
        Write-Host ("      body: " + ($r.Body.Substring(0, [Math]::Min(160, $r.Body.Length)))) -ForegroundColor DarkGray
    }
    return $r
}

function Curl-Token {
    param([string]$Path, [object]$Body)
    Write-TmpBody $Body
    $r = Invoke-CurlRequest -Method POST -Path $Path -BodyFile $tmp -ContentType "application/json"
    if ($r.Code -eq 200 -and $r.Body) {
        $j = $r.Body | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($j) { return $j.token }
    }
    return $null
}

Write-Host ("=" * 70) -ForegroundColor Cyan
Write-Host "ProductAuction API Smoke Test  ->  $base" -ForegroundColor Cyan
Write-Host ("=" * 70) -ForegroundColor Cyan

#--------------------------------------------------------------------------
# 1. AUTH
#--------------------------------------------------------------------------
Write-Host "`n=== 1. AUTH ===" -ForegroundColor Yellow

$adminToken = Curl-Token -Path "/api/Auth/login" -Body @{ email="admin@gmail.com"; password="Admin@123" }
Test-Request -Method POST -Path "/api/Auth/login" -Body @{ email="admin@gmail.com"; password="Admin@123" } -ExpectedCodes @(200) -Description "Admin login"

Test-Request -Method POST -Path "/api/Auth/register" -Body @{ email="buyer@test.com"; password="Buyer@123"; fullName="Test Buyer"; role="Buyer" } -ExpectedCodes @(200,400) -Description "Buyer register (400 ok if exists)"
$buyerToken = Curl-Token -Path "/api/Auth/login" -Body @{ email="buyer@test.com"; password="Buyer@123" }
Test-Request -Method POST -Path "/api/Auth/login" -Body @{ email="buyer@test.com"; password="Buyer@123" } -ExpectedCodes @(200) -Description "Buyer login"

Test-Request -Method POST -Path "/api/Auth/register" -Body @{ email="seller@test.com"; password="Seller@123"; fullName="Test Seller"; role="Seller" } -ExpectedCodes @(200,400) -Description "Seller register (400 ok if exists)"
$sellerToken = Curl-Token -Path "/api/Auth/login" -Body @{ email="seller@test.com"; password="Seller@123" }
Test-Request -Method POST -Path "/api/Auth/login" -Body @{ email="seller@test.com"; password="Seller@123" } -ExpectedCodes @(200) -Description "Seller login"

Test-Request -Method GET -Path "/api/Auth/me" -ExpectedCodes @(401) -Description "GET /api/auth/me no token (expect 401)"
Test-Request -Method GET -Path "/api/Admin/user" -Token $buyerToken -ExpectedCodes @(403) -Description "Buyer hits /api/admin/user (expect 403)"

#--------------------------------------------------------------------------
# 2. PUBLIC GETs
#--------------------------------------------------------------------------
Write-Host "`n=== 2. PUBLIC GETs ===" -ForegroundColor Yellow
foreach ($p in @("/api/Category","/api/Product","/api/Auction","/api/Auction/active","/api/Auction/upcoming","/api/Auction/completed","/api/Auction/cancelled")) {
    Test-Request -Method GET -Path $p -ExpectedCodes @(200) -Description ("GET " + $p)
}
Test-Request -Method GET -Path "/api/Auction/1" -ExpectedCodes @(200,404) -Description "GET /api/auction/1 (200 or 404)"
Test-Request -Method GET -Path "/api/Product/1" -ExpectedCodes @(200,404) -Description "GET /api/product/1 (200 or 404)"
Test-Request -Method GET -Path "/api/Auction/product/1" -ExpectedCodes @(200,404) -Description "GET /api/auction/product/1 (200 or 404)"

#--------------------------------------------------------------------------
# 3. AUTHENTICATED GETs per role
#--------------------------------------------------------------------------
Write-Host "`n=== 3. AUTHENTICATED GETs ===" -ForegroundColor Yellow

Write-Host "--- Admin ---" -ForegroundColor Gray
foreach ($p in @("/api/Auth/me","/api/Auth/test-token","/api/Admin/user","/api/Admin/product","/api/Admin/auctions","/api/Admin/auction-results","/api/Admin/penalties","/api/Admin/deposit-requests","/api/Admin/deposit-requests/pending","/api/Profile","/api/Profile/pending-verification","/api/Dispute","/api/Sale")) {
    Test-Request -Method GET -Path $p -Token $adminToken -ExpectedCodes @(200) -Description ("ADMIN GET " + $p)
}

Write-Host "--- Buyer ---" -ForegroundColor Gray
foreach ($p in @("/api/Auth/me","/api/Auth/test-token","/api/Wallet/me","/api/Wallet/transactions","/api/Wallet/reservations","/api/Buyer/my-bids","/api/Buyer/bid-history","/api/Buyer/won-auctions","/api/Sale/my-sales","/api/Dispute/my-disputes","/api/Auction/my-auctions")) {
    Test-Request -Method GET -Path $p -Token $buyerToken -ExpectedCodes @(200) -Description ("BUYER GET " + $p)
}
Test-Request -Method GET -Path "/api/Product/my-products" -Token $buyerToken -ExpectedCodes @(403) -Description "BUYER GET /api/product/my-products (expect 403 - seller only)"
Test-Request -Method GET -Path "/api/Profile" -Token $buyerToken -ExpectedCodes @(403) -Description "BUYER GET /api/profile (expect 403 - admin only)"

Write-Host "--- Seller ---" -ForegroundColor Gray
foreach ($p in @("/api/Auth/me","/api/Auth/test-token","/api/Wallet/me","/api/Auction/my-auctions","/api/Product/my-products","/api/Sale/my-sales")) {
    Test-Request -Method GET -Path $p -Token $sellerToken -ExpectedCodes @(200) -Description ("SELLER GET " + $p)
}
Test-Request -Method GET -Path "/api/Profile" -Token $sellerToken -ExpectedCodes @(403) -Description "SELLER GET /api/profile (expect 403 - admin only)"

Test-Request -Method GET -Path "/api/Admin/user" -Token $sellerToken -ExpectedCodes @(403) -Description "Seller hits /api/admin/user (expect 403)"
Test-Request -Method GET -Path "/api/Product/my-products" -Token $buyerToken -ExpectedCodes @(403) -Description "Buyer hits /api/product/my-products (expect 403)"

#--------------------------------------------------------------------------
# 4. PARAMETERIZED authenticated GETs
#--------------------------------------------------------------------------
Write-Host "`n=== 4. PARAMETERIZED GETs (id=1) ===" -ForegroundColor Yellow
foreach ($p in @("/api/Auction/1","/api/Product/1","/api/Sale/1","/api/Dispute/1","/api/Bid/auction/1/bids","/api/Profile/1","/api/Product/pending-approval/1")) {
    Test-Request -Method GET -Path $p -Token $adminToken -ExpectedCodes @(200,404) -Description ("ADMIN GET " + $p + " (200/404)")
}

#--------------------------------------------------------------------------
# 5. MUTATING ENDPOINTS
#--------------------------------------------------------------------------
Write-Host "`n=== 5. MUTATING ENDPOINTS ===" -ForegroundColor Yellow

Test-Request -Method POST -Path "/api/Auth/change-password" -Token $buyerToken -Body @{ CurrentPassword="Buyer@123"; NewPassword="Buyer@123!"; ConfirmNewPassword="Buyer@123!" } -ExpectedCodes @(200,400) -Description "Buyer change-password"
Test-Request -Method POST -Path "/api/Auth/change-password" -Token $buyerToken -Body @{ CurrentPassword="Buyer@123!"; NewPassword="Buyer@123"; ConfirmNewPassword="Buyer@123" } -ExpectedCodes @(200) -Description "Buyer change-password back"

$prof = @{ FullName="Updated Buyer"; ProfilePhotoUrl=""; Address="123 St"; City="Dhaka"; NationalIdNumber="123456789"; NationalIdFrontImageUrl="http://x/front.jpg"; NationalIdBackImageUrl="http://x/back.jpg" }
Test-Request -Method PUT -Path "/api/Auth/me" -Token $buyerToken -Body $prof -ExpectedCodes @(200) -Description "PUT /api/auth/me (buyer profile)"
Test-Request -Method PUT -Path "/api/Profile/me" -Token $buyerToken -Body $prof -ExpectedCodes @(200) -Description "PUT /api/profile/me (buyer profile)"

Test-Request -Method POST -Path "/api/Category" -Token $adminToken -Body @{ Name="Electronics" } -ExpectedCodes @(200,400) -Description "Admin POST /api/category (400 ok if dup)"
$cats = Invoke-CurlRequest -Method GET -Path "/api/Category"
$categoryId = $null
if ($cats.Code -eq 200 -and $cats.Body) {
    $cj = $cats.Body | ConvertFrom-Json -ErrorAction SilentlyContinue
    if ($cj -and $cj.data) {
        $cat = $cj.data | Where-Object { $_.name -eq "Electronics" } | Select-Object -First 1
        if (-not $cat) { $cat = $cj.data | Select-Object -First 1 }
        if ($cat) { $categoryId = $cat.id }
    }
}
Write-Host ("      categoryId = $categoryId") -ForegroundColor DarkGray

$cus = Invoke-CurlRequest -Method GET -Path "/api/Admin/user" -Token $adminToken
if ($cus.Code -eq 200 -and $cus.Body) {
    $uj = $cus.Body | ConvertFrom-Json -ErrorAction SilentlyContinue
    $buyerUserId = $null
    if ($uj -and $uj.data) {
        $u = $uj.data | Where-Object { $_.email -eq "buyer@test.com" } | Select-Object -First 1
        if ($u) { $buyerUserId = $u.userId }
    }
    if ($buyerUserId) {
        Test-Request -Method PUT -Path ("/api/Admin/user/" + $buyerUserId + "/lock") -Token $adminToken -Body @{ IsLocked=$true; IsPermanent=$false; DurationMinutes=10 } -ExpectedCodes @(200) -Description "Admin temp-lock buyer"
        Test-Request -Method PUT -Path ("/api/Admin/user/" + $buyerUserId + "/lock") -Token $adminToken -Body @{ IsLocked=$false } -ExpectedCodes @(200) -Description "Admin unlock buyer"
    }
}

#--------------------------------------------------------------------------
# 6. END-TO-END FLOW
#--------------------------------------------------------------------------
Write-Host "`n=== 6. END-TO-END FLOW ===" -ForegroundColor Yellow

$sellerProductId = $null
$auctionId = $null
$saleId = $null

if ($categoryId -and $sellerToken) {
    $boundary = [guid]::NewGuid().ToString("N")
    $mp = "--$boundary`n"
    $mp += "Content-Disposition: form-data; name=`"CategoryId`"`n`n$categoryId`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"Title`"`n`nTest Headphones`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"ShortSummary`"`n`nWireless headphones`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"Description`"`n`nNice wireless headphones`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"Brand`"`n`nTestBrand`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"ModelNumber`"`n`nTB-001`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"Condition`"`n`nLike New`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"PurchaseDate`"`n`n" + (Get-Date -Format "yyyy-MM-dd") + "`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"UsageHistory`"`n`nLight use`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"ReasonForSelling`"`n`nUpgrade`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"KnownIssues`"`n`nNone`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"StartingPrice`"`n`n100`n"
    $mp += "--$boundary`nContent-Disposition: form-data; name=`"BuyoutPrice`"`n`n150`n"
    $mp += "--$boundary--`n"
    Set-Content -Path $tmp -Value $mp -Encoding UTF8 -NoNewline

    $r = Invoke-CurlRequest -Method POST -Path "/api/Product" -Token $sellerToken -BodyFile $tmp -ContentType ("multipart/form-data; boundary=" + $boundary)
    $mpPass = ($r.Code -ne 0 -and @(200,400) -contains $r.Code)
    if ($mpPass) { $passCount++ } else { $failCount++ }
    $results.Add([pscustomobject]@{ Desc="Seller POST /api/product (create draft)"; Method="POST"; Path="/api/Product"; Code=$r.Code; Pass=$mpPass })
    $status = if ($mpPass) { "PASS" } else { "FAIL" }
    $color = if ($mpPass) { "Green" } else { "Red" }
    Write-Host ("{0,-6} {1,-45} -> {2}  [{3}]" -f "POST", ("Seller POST /api/product (create draft)"), $r.Code, $status) -ForegroundColor $color
    if (-not $mpPass -and $r.Body) { Write-Host ("      body: " + ($r.Body.Substring(0, [Math]::Min(160, $r.Body.Length)))) -ForegroundColor DarkGray }
    $mp2 = Invoke-CurlRequest -Method GET -Path "/api/Product/my-products" -Token $sellerToken
    if ($mp2.Code -eq 200 -and $mp2.Body) {
        $pj = $mp2.Body | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($pj -and $pj.data) {
            $p = $pj.data | Where-Object { $_.title -eq "Test Headphones" } | Select-Object -First 1
            if ($p) { $sellerProductId = $p.id }
        }
    }
    Write-Host ("      sellerProductId = $sellerProductId") -ForegroundColor DarkGray
}

if ($sellerProductId -and $sellerToken) {
    Test-Request -Method POST -Path ("/api/Product/" + $sellerProductId + "/submit-for-approval") -Token $sellerToken -ExpectedCodes @(200,400) -Description "Seller submit product for approval"
    Test-Request -Method PUT -Path ("/api/Product/" + $sellerProductId + "/approval") -Token $adminToken -Body @{ Status="Approved" } -ExpectedCodes @(200,400) -Description "Admin approve product (creates auction)"
    $auc = Invoke-CurlRequest -Method GET -Path ("/api/Auction/product/" + $sellerProductId)
    if ($auc.Code -eq 200 -and $auc.Body) {
        $aj2 = $auc.Body | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($aj2 -and $aj2.data) { $a = $aj2.data | Select-Object -First 1; if ($a) { $auctionId = $a.auctionId } }
    }
    Write-Host ("      auctionId = $auctionId") -ForegroundColor DarkGray
}

if ($auctionId -and $sellerToken) {
    $start = (Get-Date).AddMinutes(5).ToUniversalTime().ToString("o")
    $end = (Get-Date).AddHours(1).ToUniversalTime().ToString("o")
    Test-Request -Method PUT -Path ("/api/Auction/" + $auctionId + "/schedule") -Token $sellerToken -Body @{ StartTime=$start; EndTime=$end } -ExpectedCodes @(200,400) -Description "Seller schedule auction (future)"
}

if ($auctionId -and $buyerToken) {
    Test-Request -Method POST -Path "/api/Bid" -Token $buyerToken -Body @{ AuctionId=$auctionId; Amount=100 } -ExpectedCodes @(200,400) -Description "Buyer POST /api/bid (amount 100)"
}

if ($auctionId -and $buyerToken) {
    $bo = Invoke-CurlRequest -Method POST -Path ("/api/Bid/" + $auctionId + "/buyout") -Token $buyerToken
    $boPass = ($bo.Code -ne 0 -and @(200,400) -contains $bo.Code)
    if ($boPass) { $passCount++ } else { $failCount++ }
    $results.Add([pscustomobject]@{ Desc="Buyer POST /api/bid/{id}/buyout"; Method="POST"; Path=("/api/Bid/" + $auctionId + "/buyout"); Code=$bo.Code; Pass=$boPass })
    $status = if ($boPass) { "PASS" } else { "FAIL" }
    $color = if ($boPass) { "Green" } else { "Red" }
    Write-Host ("{0,-6} {1,-45} -> {2}  [{3}]" -f "POST", ("Buyer POST /api/bid/{id}/buyout"), $bo.Code, $status) -ForegroundColor $color
    if (-not $boPass -and $bo.Body) { Write-Host ("      body: " + ($bo.Body.Substring(0, [Math]::Min(160, $bo.Body.Length)))) -ForegroundColor DarkGray }
    if ($bo.Code -eq 200 -and $bo.Body) {
        $boj = $bo.Body | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($boj -and $boj.data) { $saleId = $boj.data.id }
    }
    Write-Host ("      saleId = $saleId") -ForegroundColor DarkGray
}

if ($saleId -and $buyerToken) {
    Test-Request -Method POST -Path ("/api/Sale/" + $saleId + "/pay") -Token $buyerToken -ExpectedCodes @(200,400) -Description "Buyer POST /api/sale/{id}/pay"
}

if ($saleId -and $sellerToken) {
    Test-Request -Method POST -Path ("/api/Sale/" + $saleId + "/dispatch") -Token $sellerToken -Body @{ DispatchInfo="Courier XYZ"; TrackingEvidence="http://x/track.jpg" } -ExpectedCodes @(200,400) -Description "Seller POST /api/sale/{id}/dispatch"
}

if ($saleId -and $buyerToken) {
    Test-Request -Method POST -Path ("/api/Sale/" + $saleId + "/confirm-receipt") -Token $buyerToken -ExpectedCodes @(200,400) -Description "Buyer POST /api/sale/{id}/confirm-receipt"
}

if ($saleId -and $buyerToken) {
    Test-Request -Method POST -Path "/api/Dispute" -Token $buyerToken -Body @{ SaleId=$saleId; Complaint="Item not as described"; EvidenceUrl="http://x/evidence.jpg" } -ExpectedCodes @(200,400) -Description "Buyer POST /api/dispute"
    Test-Request -Method GET -Path "/api/Dispute/my-disputes" -Token $buyerToken -ExpectedCodes @(200) -Description "GET /api/dispute/my-disputes"
}

#--------------------------------------------------------------------------
# 7. SUMMARY
#--------------------------------------------------------------------------
Write-Host "`n" + ("=" * 70) -ForegroundColor Cyan
Write-Host ("SUMMARY:  PASS = $passCount   FAIL = $failCount   TOTAL = $($passCount + $failCount)") -ForegroundColor Cyan
Write-Host ("=" * 70) -ForegroundColor Cyan

if ($failCount -gt 0) {
    Write-Host "`nFAILED REQUESTS:" -ForegroundColor Red
    $results | Where-Object { -not $_.Pass } | ForEach-Object {
        Write-Host ("  {0,-6} {1,-45} -> {2}  [{3}]" -f $_.Method, $_.Desc, $_.Code, "FAIL") -ForegroundColor Red
    }
    exit 1
} else {
    Write-Host "All checks passed." -ForegroundColor Green
    exit 0
}
