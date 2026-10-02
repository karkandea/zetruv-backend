# Customer Payment Readiness

Figma: Digital Purchase Flow node 3064:8. Backend/CMS only; no customer UI implementation.

The existing `cms/site/payment-methods` rows are *presentation configuration*, not proof a channel can collect funds. Prior to this change, the only registered gateway was `mock`, which issues `mock://` links; it cannot accept live QRIS, VA, or e-wallet payments.

New endpoints:
- Public `GET /api/v1/checkout/payment-options`: `canAcceptLivePayments`, `status`, `availableMethods`. No secrets or nonoperational methods in the public options list.
- Authenticated CMS `GET /api/v1/cms/payments/readiness`: provider name, actual readiness, and per-display-method `cmsEnabled`, `operational`, `reason`.

Only gateways that deliberately implement `ILivePaymentChannelGateway`, advertise `IsLiveConfigured=true`, and report `SupportsChannel(code)` can make a CMS method operational. The mock gateway does **not** implement this interface. Unregistered methods and inactive CMS methods are not customer-payable. No provider credentials are returned in either response.

`Unconfigured` = gateway unresolved; `DemoOnly` = mock/non-live gateway; `NoSupportedMethods` = live-capable adapter exists but no CMS method operational; `Ready` = at least one live supported CMS method.

Critical gap still open: actual Xendit/payment-provider adapter, channel choice in payment-initiation contract, provider webhook/reconciliation integration, and customer FE UI. Existing legacy mock-only payment initiation is retained for development smoke tests; frontend must use the readiness endpoint to withhold live payment choices. This PR does not claim QRIS/VA/GoPay are integrated.

Regression: `smoke-payment-order-access.sh` asserts zero customer-payable options with mock gateway while preserving successful mock payment access-token behavior.
