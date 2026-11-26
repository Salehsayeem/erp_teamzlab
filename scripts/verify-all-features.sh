#!/usr/bin/env bash
# T3 live verification: insert/get smoke of every endpoint against the real PostgreSQL dev DB.
# Usage:  API_BASE=http://localhost:5000 scripts/verify-all-features.sh
# Needs:  curl + node on PATH; the API running against the migrated PG* database from .env
#         (dotnet ef database update --project apps/api, then dotnet run --project apps/api).
# Flow is self-contained: creates its own supplier/PO/customer/SO/inventory rows (SMOKE-* tags).
set -u
API_BASE="${API_BASE:-http://localhost:5000}"
PASS=0 FAIL=0

ok()  { PASS=$((PASS+1)); printf '  PASS  %s\n' "$*"; }
bad() { FAIL=$((FAIL+1)); printf '  FAIL  %s\n' "$*"; }
say() { printf '\n== %s\n' "$*"; }

# jget '<js expression over j>'  (JSON on stdin; __MISSING__ when path absent)
jget() { node -e "const j=JSON.parse(require('fs').readFileSync(0,'utf8'));const v=($1);console.log(v===undefined?'__MISSING__':typeof v==='object'?JSON.stringify(v):v);"; }

# req METHOD PATH TOKEN [JSONBODY] [EXTRA-HEADER] -> STATUS, BODY
req() {
  local m=$1 p=$2 t=$3 b=${4:-} h=${5:-}
  local args=(-s -w $'\n%{http_code}' -X "$m" "$API_BASE$p" -H "Authorization: Bearer $t")
  [ -n "$b" ] && args+=(-H "Content-Type: application/json" -d "$b")
  [ -n "$h" ] && args+=(-H "$h")
  local out; out=$(curl "${args[@]}" || printf '\n000')
  STATUS=${out##*$'\n'}; BODY=${out%$'\n'*}
}
expect_status() { # CODE LABEL
  if [ "$STATUS" = "$1" ]; then ok "$2 -> $STATUS"; else bad "$2: expected $1 got $STATUS — $BODY"; fi
}
expect_json() { # EXPR WANT LABEL
  local got; got=$(printf '%s' "$BODY" | jget "$1" 2>/dev/null || echo __PARSE_ERROR__)
  if [ "$got" = "$2" ]; then ok "$3"; else bad "$3: expected $2 got $got"; fi
}

# stub auth: the token IS the seeded user id (admin U-001, procurement U-002, finance U-003)
ADMIN=U-001; PROC=U-002; FIN=U-003; SALES=U-004
RUN="$(date +%s)-$RANDOM"
SKU="SMOKE-SKU-$RUN"

say "auth + platform"
req POST /api/v1/auth/token-exchange "" '{"email":"admin@teamzlab.dev"}'
expect_status 200 "POST /auth/token-exchange"
req GET /api/v1/me "$ADMIN"
expect_status 200 "GET /me"
expect_json 'j.data.user.id' "U-001" "/me resolves the bearer token to the admin user"
req GET /api/v1/entities "$ADMIN"
expect_status 200 "GET /entities"
expect_json 'j.data.length >= 1' "true" "/entities lists the seeded legal entity"
req GET /api/v1/audit-events "$PROC"
expect_status 403 "GET /audit-events is admin-only (finding 6)"

say "suppliers"
req POST /api/v1/suppliers "$PROC" "{\"name\":\"Smoke Co\",\"taxId\":\"SMOKE-$RUN\",\"email\":\"smoke-$RUN@sup.test\"}"
expect_status 201 "POST /suppliers"
SUP=$(printf '%s' "$BODY" | jget 'j.data.id')
req GET /api/v1/suppliers "$PROC"
expect_status 200 "GET /suppliers"
expect_json "Array.isArray(j.data) && j.data.some(s=>s.id==='$SUP')" "true" "supplier persisted and listed"

say "purchase order flow (maker-checker)"
req POST /api/v1/purchase-orders "$PROC" "{\"supplierId\":\"$SUP\",\"lines\":[{\"itemSku\":\"$SKU\",\"description\":\"Smoke item\",\"qty\":20,\"unitPrice\":2.5}]}"
expect_status 201 "POST /purchase-orders"
PO=$(printf '%s' "$BODY" | jget 'j.data.id')
req POST "/api/v1/purchase-orders/$PO/submit" "$PROC" '{}'
expect_status 200 "POST /purchase-orders/{id}/submit"
req POST "/api/v1/purchase-orders/$PO/approvals" "$PROC" '{}'
expect_status 409 "creator cannot approve own PO (maker-checker)"
req POST "/api/v1/purchase-orders/$PO/approvals" "$ADMIN" '{}'
expect_status 200 "checker approves PO"
expect_json 'j.data.status' "Approved" "PO status Approved"

say "receipt -> stock + journal + outbox (idempotent)"
req POST /api/v1/receipts "$PROC" "{\"purchaseOrderId\":\"$PO\",\"itemSku\":\"$SKU\",\"qty\":10}" "Idempotency-Key: $RUN-rc"
expect_status 201 "POST /receipts (Idempotency-Key)"
RC1=$(printf '%s' "$BODY" | jget 'j.data.receipt.id')
JRN=$(printf '%s' "$BODY" | jget 'j.data.journal.id')
req POST /api/v1/receipts "$PROC" "{\"purchaseOrderId\":\"$PO\",\"itemSku\":\"$SKU\",\"qty\":10}" "Idempotency-Key: $RUN-rc"
expect_status 201 "replay same Idempotency-Key -> 201"
expect_json 'j.data.receipt.id' "$RC1" "replay returns the same receipt (no second write)"
req GET /api/v1/stock "$ADMIN"
expect_status 200 "GET /stock"
expect_json "(j.data.find(s=>s.itemSku==='$SKU')||{}).qtyOnHand" "10" "stock incremented once (default receiving location)"
req GET /api/v1/journals "$FIN"
expect_status 200 "GET /journals (finance role)"
expect_json "(()=>{const jr=j.data.find(x=>x.id==='$JRN');if(!jr)return false;const d=jr.lines.reduce((a,l)=>a+l.debit,0);const c=jr.lines.reduce((a,l)=>a+l.credit,0);return Math.abs(d-c)<0.001;})()" "true" "receipt journal is balanced (debit == credit)"
req POST /api/v1/receipts "$PROC" "{\"purchaseOrderId\":\"$PO\",\"itemSku\":\"$SKU\",\"qty\":11}"
expect_status 409 "over-receipt rejected (11 requested + 10 received > 20 ordered)"
req GET /api/v1/outbox "$ADMIN"
expect_status 200 "GET /outbox"
expect_json 'j.data.length >= 1' "true" "outbox emitted events"

say "journal reversal unwinds stock"
req POST "/api/v1/journals/$JRN/reversals" "$FIN" '{}'
expect_status 201 "POST /journals/{id}/reversals"
req GET /api/v1/stock "$ADMIN"
expect_json "(j.data.find(s=>s.itemSku==='$SKU')||{}).qtyOnHand" "0" "reversal unwinds the receipt's stock"
req POST /api/v1/receipts "$PROC" "{\"purchaseOrderId\":\"$PO\",\"itemSku\":\"$SKU\",\"qty\":10}" "Idempotency-Key: $RUN-rc2"
expect_status 201 "second receipt (fresh key) restores stock for the sales flow"

say "customers + sales order flow (U-004 sales rep owns so:write; admin checks)"
req POST /api/v1/customers "$SALES" "{\"name\":\"Smoke Buyer\",\"taxId\":\"SMOKE-B-$RUN\",\"email\":\"buyer-$RUN@cust.test\"}"
expect_status 201 "POST /customers"
CUS=$(printf '%s' "$BODY" | jget 'j.data.id')
req POST /api/v1/sales-orders "$SALES" "{\"customerId\":\"$CUS\",\"lines\":[{\"itemSku\":\"$SKU\",\"description\":\"Smoke item\",\"qty\":3,\"unitPrice\":4}]}"
expect_status 201 "POST /sales-orders"
SO=$(printf '%s' "$BODY" | jget 'j.data.id')
req POST "/api/v1/sales-orders/$SO/submit" "$SALES" '{}'
expect_status 200 "POST /sales-orders/{id}/submit"
req POST "/api/v1/sales-orders/$SO/approvals" "$SALES" '{}'
expect_status 409 "creator cannot approve own SO (maker-checker)"
req POST "/api/v1/sales-orders/$SO/approvals" "$ADMIN" '{}'
expect_status 200 "checker approves SO"
req POST "/api/v1/sales-orders/$SO/shipments" "$SALES" "{\"itemSku\":\"$SKU\",\"qty\":3}"
expect_status 201 "POST /sales-orders/{id}/shipments (COGS journal posted)"
req GET /api/v1/stock "$ADMIN"
expect_json "(j.data.find(s=>s.itemSku==='$SKU')||{}).qtyOnHand" "7" "shipment drew stock down 10 -> 7"
# R-T7f: the journal-only invoice endpoint is retired — invoices are documents now:
# SALES drafts from the shipment source event, FINANCE (checker) posts the AR journal
req POST "/api/v1/sales-orders/$SO/invoice-drafts" "$SALES" '{}'
expect_status 201 "POST /sales-orders/{id}/invoice-drafts (invoice document draft)"
INV=$(printf '%s' "$BODY" | jget 'j.data.id' 2>/dev/null || echo "")
req POST "/api/v1/invoices/$INV/postings" "$FIN" '{}'
expect_status 200 "POST /invoices/{id}/postings (checker posts the AR journal)"

say "inventory: warehouses, locations, transfers, counts"
req POST /api/v1/warehouses "$PROC" "{\"code\":\"WH-S-$RUN\",\"name\":\"Smoke WH\"}"
expect_status 201 "POST /stock/warehouses"
WH=$(printf '%s' "$BODY" | jget 'j.data.id')
req POST /api/v1/locations "$PROC" "{\"warehouseId\":\"$WH\",\"code\":\"LOC-A-$RUN\",\"name\":\"Smoke A\"}"
expect_status 201 "POST /stock/locations A"
LA=$(printf '%s' "$BODY" | jget 'j.data.id')
req POST /api/v1/locations "$PROC" "{\"warehouseId\":\"$WH\",\"code\":\"LOC-B-$RUN\",\"name\":\"Smoke B\"}"
expect_status 201 "POST /stock/locations B"
LB=$(printf '%s' "$BODY" | jget 'j.data.id')
req GET /api/v1/locations "$PROC"
expect_status 200 "GET /stock/locations"
req GET /api/v1/warehouses "$PROC"
expect_status 200 "GET /stock/warehouses"
req GET /api/v1/stock/valuation "$FIN"
expect_status 200 "GET /stock/valuation"
req POST /api/v1/stock/transfers "$PROC" "{\"fromLocationId\":\"LOC-A1\",\"toLocationId\":\"$LB\",\"itemSku\":\"$SKU\",\"qty\":2}"
expect_status 201 "POST /stock/transfers (LOC-A1 -> new location)"
req POST /api/v1/stock/counts "$PROC" "{\"locationId\":\"$LB\",\"items\":[{\"itemSku\":\"$SKU\",\"countedQty\":2}]}"
expect_status 201 "POST /stock/counts"
CNT=$(printf '%s' "$BODY" | jget 'j.data.id')
req POST "/api/v1/stock/counts/$CNT/close" "$PROC" '{}'
expect_status 409 "counter cannot close own count (maker-checker)"
req POST "/api/v1/stock/counts/$CNT/close" "$ADMIN" '{}'
expect_status 201 "checker closes count (Created, with adjustment payload)"

say "procurement deferred wave: requisitions, blanket, ASN, screening, analytics"
req POST /api/v1/procurement-categories "$PROC" "{\"name\":\"Smoke cat $RUN\"}"
expect_status 201 "POST /procurement-categories"
CAT=$(printf '%s' "$BODY" | jget 'j.data.id')
req POST /api/v1/unit-of-measures "$PROC" "{\"code\":\"EA-S-$RUN\",\"name\":\"Each\",\"dimension\":\"Count\",\"precision\":0,\"isBase\":true}"
expect_status 201 "POST /unit-of-measures"
UNIT=$(printf '%s' "$BODY" | jget 'j.data.code')
req POST /api/v1/requisitions "$PROC" "{\"purpose\":\"Smoke req $RUN\",\"currency\":\"BDT\",\"lines\":[{\"categoryId\":\"$CAT\",\"description\":\"smoke line\",\"qty\":2,\"unitPrice\":50,\"unit\":\"$UNIT\",\"needBy\":\"2030-01-01\"}]}"
expect_status 201 "POST /requisitions (draft saves incomplete)"
REQ=$(printf '%s' "$BODY" | jget 'j.data.id')
req POST "/api/v1/requisitions/$REQ/decisions" "$PROC" '{"decision":"Approve","version":1}'
expect_status 403 "creator without requisitions:approve is 403 FORBIDDEN (permission-first)"
req POST /api/v1/requisitions "$ADMIN" "{\"purpose\":\"Smoke req admin $RUN\",\"currency\":\"BDT\",\"lines\":[{\"categoryId\":\"$CAT\",\"description\":\"smoke line\",\"qty\":2,\"unitPrice\":50,\"unit\":\"$UNIT\",\"needBy\":\"2030-01-01\"}]}"
expect_status 201 "POST /requisitions as admin (approver-creator)"
REQA=$(printf '%s' "$BODY" | jget 'j.data.id')
req POST "/api/v1/requisitions/$REQA/submission" "$ADMIN" '{}'
expect_status 200 "submit admin requisition"
req POST "/api/v1/requisitions/$REQA/decisions" "$ADMIN" '{"decision":"Approve","version":1}'
expect_status 409 "approver-creator cannot decide own requisition (maker-checker, 016)"
expect_json 'j.error.code' "REQUISITION_APPROVAL_ERROR" "creator-block emits REQUISITION_APPROVAL_ERROR"
req POST "/api/v1/requisitions/$REQ/submission" "$PROC" '{}'
expect_status 200 "submit (budget Pass + route resolves)"
req GET "/api/v1/requisitions/$REQ" "$FIN"
expect_status 200 "GET requisition detail"
VER=$(printf '%s' "$BODY" | jget 'j.data.step.version')
req POST "/api/v1/requisitions/$REQ/decisions" "$FIN" "{\"decision\":\"Approve\",\"version\":$VER}"
expect_status 200 "finance decides (018 versioned token)"
req POST /api/v1/blanket-orders "$PROC" "{\"supplierId\":\"$SUP\",\"currency\":\"BDT\",\"ceilingAmount\":1000,\"validFrom\":\"2026-01-01\",\"validTo\":\"2030-01-01\"}"
expect_status 201 "POST /blanket-orders"
BKO=$(printf '%s' "$BODY" | jget 'j.data.id')
req POST "/api/v1/blanket-orders/$BKO/releases" "$PROC" '{"amount":400}'
expect_status 201 "POST blanket release (ceiling debit)"
req POST "/api/v1/blanket-orders/$BKO/releases" "$PROC" '{"amount":900}'
expect_status 409 "over-ceiling release conflicts (BLANKET_CEILING_CONFLICT)"
req GET "/api/v1/procurement-analytics/spend?groupBy=supplier" "$PROC"
expect_status 200 "GET analytics spend"
req GET /api/v1/procurement-analytics/demand "$FIN"
expect_status 200 "GET analytics demand (per-currency rows)"
