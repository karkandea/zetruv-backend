# eFootball Mobile and CODM Garena — provider fulfillment decision

Date: 2026-11-27. Scope: DEV readiness and synthetic E2E only. No approval to activate live sales.

## Verified routes

| Game | Publisher-approved consumer route | B2B next step |
| --- | --- | --- |
| eFootball Mobile Indonesia | https://efootball.codashop.com/id-id/efootball — customer signs in on official KONAMI/Coda site. Mobile purchase requires the linked KONAMI ID; do not obtain credentials. | Ask Coda Distribution for authorization for exact Indonesian iOS/Android SKUs and a supported delegated or direct fulfillment contract. |
| CODM Garena Indonesia | https://kiosgamer.co.id/?app=100082 and https://www.unipin.com/id/garena/call-of-duty — player identifier-based top-up without account password. | Request UniPin reseller/API access and confirm supported Garena Indonesia CP packages, exact OpenID/Player ID format and lookup, test credentials, settlement and refund terms. A move to AUTO_ID needs PO approval and validated mapping. |

Sources:
- KONAMI terms: https://legal.konami.com/games/efootball/terms/tou/id.html
- Official KONAMI store terms (personal consumption, not resale): https://konami-webstore-support.zendesk.com/hc/en-us/articles/13627696731535-eFootball-Terms-and-Conditions-of-Sale-Indonesia
- Garena terms: https://content.garena.com/legal/tos/tos_en.html
- Coda merchant contact: https://www.coda.co/contact/
- UniPin partner portal: https://corp.unipin.com/en/products/reseller-platform (bd@unipin.com).

Public consumer stores are references, not authorization to resell or provider API contracts. No assumption that Coda offers delegated authentication or that either supplier exposes these exact SKUs through its B2B API. Never use a customer password/OTP or consumer checkout automation.

## Current DEV evidence (2026-11-27)
- Both seeded product slugs return 404 from public catalog.
- Readiness endpoint returns NotReady / None for c2000000-0000-0000-0000-000000000001 and c2000000-0000-0000-0000-000000000002.
- Existing seeded Small/Medium/Large SKUs are dummy entries, not approved supplier SKUs. Do not publish or price them based on consumer promotions.
- Existing checkout and CMS payment/fulfillment protections remain unchanged.

## Acceptance before enabling a product
1. Written permission and signed commercial contract for exact game/platform/region and purchase method.
2. Approved authentication/target scheme, SKU and price mapping, sandbox credentials through secret storage, supported payment/fulfillment status and signed callback or authenticated polling.
3. Persist one stable reference per order item; dispatch only after verified Paid; retry uncertain provider outcomes with same reference. Mark Completed only after confirmed delivery; failure/expiry/reconciliation/refund must not create a duplicate purchase.
4. Synthetic isolated tests: unavailable products rejected, payment failure/no dispatch, paid once, pending/success/failure, wrong target, duplicate event, timeout and recovery. Provider sandbox E2E and verified delivery evidence are separate gates.
5. DEV smoke, PO review, then explicitly approved activation. Do not switch CODM fulfillment type or turn on existing login readiness just to pass E2E.

This patch adds a guard regression to the existing synthetic manual-login smoke: even with global readiness switches enabled and active seeded products, both products must remain NotReady without their individual authorization. No real player accounts, payment, provider calls, catalog activation, or new credential collection.
