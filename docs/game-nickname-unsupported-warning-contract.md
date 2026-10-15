# Figma — Provider without nickname verification

Reference: Digital Purchase Flow Figma node 3064:8 → Other States & Handoff → Product Detail State — Dynamic Account Fields.

When CMS has an **active** `provider_game_mappings` entry for an AUTO_ID game's provider with `NicknameCheckEnabled=false`, its account destination fields still use `ProductInputFieldRules` (mandatory dynamic fields and sensitive-key rejection), but the backend **must not invent a nickname or claim external verification**.

`POST /api/v1/game-account/validate`:

1. Submit `{"productId":"...","fields":{"userid":"...","zoneid":"..."}}` without acknowledgment. Returns **409** `{"code":"NICKNAME_VERIFICATION_UNSUPPORTED", "message":"..."}` and **does not** issue or persist a validation.
2. Show Figma warning to the user; after the customer explicitly checks the destination, repeat with `acknowledgeUnverifiedAccount:true` and the same fields.
3. With an active mapped **configured fulfillment adapter**, backend returns **200** `{"status":"Warning","validationId":"...","accountDisplayName":null,"warning":"..."}`. The validation uses the existing 10-minute expiry and checkout one-time-use rules. ProviderReference is `null`; **no provider lookup, reference or nickname is fabricated**.
4. Missing/inactive fulfillment adapter returns 503, never falls back to a fake successful verification. Production must reject mock provider mapping through the environment check.
5. Games with ordinary nickname checks configured, or without an explicit game-level capability mapping, keep their existing validator flow (`Verified` / `ACCOUNT_NOT_FOUND` / `VALIDATION_UNAVAILABLE`). A real provider error must **never** fall back to self-attestation.

Integration regression in `scripts/smoke-provider-mapping-runtime.sh` covers 409 before confirmation, no write, valid 200 Warning after confirmation with null provider reference/display name, then authorized mock checkout and actual provider-mapped fulfillment. This is API contract parity only; customer FE still needs to display the warning.
