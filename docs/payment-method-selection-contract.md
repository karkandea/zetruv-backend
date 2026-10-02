# Backend payment method selection — Digital Purchase Figma

This backend-only API contract prepares QRIS, BCA Virtual Account and GoPay for a future verified Xendit Payments API v3 gateway, without treating CMS display configuration as a real payment capability.

- First get `GET /api/v1/checkout/payment-options`. It only returns CMS-enabled methods supported by a live configured gateway.
- Create the order normally; then call `POST /api/v1/checkout/orders/{orderId}/payment?methodCode=<code>` with `X-Order-Access-Token`.
- When a live gateway is selected, methodCode is REQUIRED, must be supported by that provider, and must have an active CMS SitePaymentMethod entry. Unsupported/disabled/unconfigured methods are rejected BEFORE reserving inventory or calling the provider. The normalized uppercase code is sent as `PaymentGatewayCreateRequest.MethodCode`.
- Mock remains available for existing non-production integration tests ONLY when methodCode is absent; mock rejects selected QRIS / BCA VA / GoPay instead of producing pretend payment details.
- When an active pending attempt already exists, omit methodCode to recover the existing session; a different method may be chosen after failure/expiry and a fresh payment attempt is created on the SAME order. Existing live provider switching while active requires a separate provider cancellation flow (not implemented).
- Real Xendit credentials, a live provider adapter, webhook token, gateway testing, channel activation and end-to-end sandbox tests are NOT part of this PR and remain blockers. Do not advertise this contract as live payment support. 
