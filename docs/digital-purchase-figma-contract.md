# Zetruv — Digital Purchase Figma / backend parity matrix

Source of truth: [Figma Digital Purchase Flow, node 3064:8](https://www.figma.com/design/HVAurT3YUzZfZb7b5yUBla/Zetruv---Redesign--Web-Phase-1-?node-id=3064-8).
Reviewed against Figma sections "Top Up Via ID", "Top Up Via Login", "Game Accounts", "Joki Game", "Voucher Game", "Validation States", "Payment States", and "Other States & Handoff" on **2 October 2026**.

**Scope of this matrix:** REST API/business rules and existing CMS backend. Matching visual pixels/layouts, user-facing FE routing, and payment-provider sandbox/live activation are NOT validated here. Do not call the end-to-end product "1:1" until those separately pass QA.

| Figma flow/state | Backend contract & evidence | State |
| --- | --- | --- |
| Browse categories, grouped Diamonds/Starlight SKU, sale price | Public catalog/PDP + CMS groupName. `smoke-catalog-pdp-contract.sh` | Implemented |
| Top Up Via ID: dynamic User ID/Zone fields, validation errors, verified nickname | `POST /api/v1/game-account/validate`; field-scoped schema, verified/404/503 results. `smoke-storefront-scope.sh`, `smoke-auto-id-fulfillment.sh` | Implemented for configured adapter; real game nickname provider is not enabled |
| Top Up Via Login: login credentials required, encrypted at checkout, clear after fulfillment | `CheckoutService`, `ManualLoginCredentialService`; `smoke-manual-login-credentials.sh` | Implemented |
| Same SKU for different destination accounts | Per-target stable cart lines, protected CMS order target snapshots and owner-only order detail, PRs #64, #65, #71. `smoke-storefront-scope.sh`, `smoke-customer-auth.sh` | Implemented |
| Cart: change User ID/Zone resets verification of **only edited line**; re-verify restores eligibility | `PATCH /api/v1/me/cart/lines/{cartItemId}/target`, explicit `targetStatus` and per-line availability, PR #74 | **In review / CI**, not yet in dev |
| Cart: empty/no selection, selected item checkboxes, disabled buttons | Cart API returns lines and `isAvailable`; selection and button state are customer FE behavior | FE scope, not backend-verified |
| Login gate before ALL digital checkouts | Strict `POST /api/v1/checkout/verified/orders` proposed in PR #75; customer JWT + verified email + order ownership | **In review / CI**. Legacy `POST /api/v1/checkout/orders` still allows some guest digital purchases; strict Figma gate **not globally enforced** |
| Checkout voucher code apply/revoke/usage concurrency | `DiscountVoucherService`, voucher preview, atomic claim & cancellation release. `smoke-storefront-scope.sh` | Implemented |
| Checkout create error/double click/idempotency | Verified principal-scoped UUID v4 key, replay and request hash, locking. `smoke-storefront-scope.sh` | Implemented for signed-in client |
| Checkout PRICE CHANGE / acknowledgment | `expectedUnitPrice`, HTTP 409 `PRICE_CHANGED`, and latest-priced ACK enforcement, PRs #68/#73. `smoke-catalog-pdp-contract.sh` | Implemented |
| Game Account: unique stock, detail attributes, owner purchase/history | Per-game/listing typed fields, stock=1, reservation, personalized history. `smoke-storefront-scope.sh` | Implemented |
| Joki/Manual fulfillment; AUTO_ID provider fulfillment | Manual post-paid fulfillment, encrypted login, mapped AUTO_ID execution and retry. Provider authority blocked from generic CMS override, PR #72 | Implemented for configured/mock providers; real fulfillment adapter separate |
| Voucher Game: secure code stock and post-paid reveal | Encrypted inventory, paid allocation and customer authorized reveal. `smoke-game-voucher-inventory.sh` | Implemented |
| Payment method not selected: Pay disabled; only live methods advertised | `GET /api/v1/checkout/payment-options`; mock shows zero payable channels, PR #66; methodCode validates provider/CMS, PR #70 | Backend readiness implemented, no real payable method enabled |
| Payment Pending → Paid / Failed / Expired; retry on same order | `GET/POST /api/v1/checkout/orders/{id}/payment`, signed order access token, PR #69, webhook/reconciliation; `smoke-payment-order-access.sh` | Implemented in mock/integration tests |
| Manual CMS spoofing of Paid and premature fulfillment blocked | Payment route HTTP 410 (#67), fulfillment authority (#72); smoke CI | Implemented |
| VA number, QRIS QR, GoPay deeplink and verified provider delivery | No configured real Xendit gateway. No mock QR/VA details shown as payable, and credentials are not invented | **Explicitly parked by project owner** |
| Figma provider cannot check nickname: show warning and allow user decision | Capability + explicit warning/acknowledgment API contract absent | **To Do**; do not treat provider outage as successful verification |
| "Unduh Invoice" in pending/paid payment states | No authorized invoice download endpoint identified | **To Do** |
| Live/sandbox payment E2E | Requires Xendit sandbox credentials/channels and external verification | **Parked with Xendit** |

## Release gates
1. Merge only after `.NET build`, migration check, and backend integration suite pass.
2. DEV deployment workflow must complete successfully; PR merge alone is **not** verification of deployed behavior.
3. For API/client parity, customer frontend must adopt strict checkout route, per-line re-verification, payment status polling, and checkout states. **This backend-only project must not edit FE.**
4. No claims of successful live payment, nickname provider verification, invoice download, or pixel-level 1:1 while those are not implemented/tested.

## Immediate non-Xendit queue
- PR #74: cart edit and re-verification, integration QA then merge.
- PR #75: strict verified checkout entrypoint, integration QA then merge; legacy route cutover **separate** and requires FE migration.
- Backend provider nickname-support capability with warning/acknowledgment state.
- Authorized invoice data/download contract.

Historical TODO items for discount coupons, encrypted Game Voucher inventory, order idempotency, price change, payment polling, and multi-target cart are **not open anymore**; this document supersedes the September initial-gap checklist.
