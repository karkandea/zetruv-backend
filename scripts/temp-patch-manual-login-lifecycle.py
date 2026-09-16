from pathlib import Path

# 1) Payment initiation + paid webhook load MANUAL_LOGIN credential state and
# block a new/recovered payment when the credential payload is no longer usable.
path = Path('src/Zetruv.Api/Features/Payments/PaymentService.cs')
text = path.read_text()
old = '''        var order = await db.Orders\n            .Include(x => x.Items)\n            .Include(x => x.Transactions)\n            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);\n'''
new = '''        var order = await db.Orders\n            .Include(x => x.Items)\n                .ThenInclude(x => x.ManualLoginCredential)\n            .Include(x => x.Transactions)\n            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);\n'''
if old not in text:
    raise SystemExit('PaymentService initiate include anchor not found')
text = text.replace(old, new, 1)

old = '''        var now = DateTimeOffset.UtcNow;\n        var pendingPayments = order.Transactions\n'''
new = '''        var now = DateTimeOffset.UtcNow;\n        var unavailableManualLogin = order.Items.FirstOrDefault(x =>\n            x.FulfillmentMethod == FulfillmentMethod.MANUAL_LOGIN &&\n            !ManualLoginCredentialService.IsUsable(x.ManualLoginCredential, now));\n        if (unavailableManualLogin is not null)\n        {\n            return InitiatePaymentResult.Failure(\n                $"Login credentials for {unavailableManualLogin.ProductName} expired or were cleared. Create a new order before paying.");\n        }\n\n        var pendingPayments = order.Transactions\n'''
if old not in text:
    raise SystemExit('PaymentService manual-login guard anchor not found')
text = text.replace(old, new, 1)

old = '''        var paymentTransaction = await db.PaymentTransactions\n            .Include(x => x.Order)\n                .ThenInclude(x => x.Items)\n            .SingleOrDefaultAsync(x =>\n'''
new = '''        var paymentTransaction = await db.PaymentTransactions\n            .Include(x => x.Order)\n                .ThenInclude(x => x.Items)\n                    .ThenInclude(x => x.ManualLoginCredential)\n            .SingleOrDefaultAsync(x =>\n'''
if old not in text:
    raise SystemExit('PaymentService webhook include anchor not found')
text = text.replace(old, new, 1)
path.write_text(text)

# 2) Credential service: expose expiry on reveal, centralize usability, and turn
# paid unfulfillable MANUAL_LOGIN items into an explicit Failed state on expiry.
path = Path('src/Zetruv.Api/Features/Orders/ManualLoginCredentials.cs')
text = path.read_text()
old = '''public sealed record ManualLoginCredentialRevealResponse(\n    Guid OrderId,\n    Guid OrderItemId,\n    IReadOnlyDictionary<string, string> Fields,\n    int RevealCount,\n    DateTimeOffset RevealedAt);\n'''
new = '''public sealed record ManualLoginCredentialRevealResponse(\n    Guid OrderId,\n    Guid OrderItemId,\n    IReadOnlyDictionary<string, string> Fields,\n    int RevealCount,\n    DateTimeOffset RevealedAt,\n    DateTimeOffset ExpiresAt);\n'''
if old not in text:
    raise SystemExit('Reveal response anchor not found')
text = text.replace(old, new, 1)

old = '''                fields,\n                credential.RevealCount,\n                now),\n            null);\n'''
new = '''                fields,\n                credential.RevealCount,\n                now,\n                credential.ExpiresAt),\n            null);\n'''
if old not in text:
    raise SystemExit('Reveal response construction anchor not found')
text = text.replace(old, new, 1)

old = '''    public async Task<int> ClearExpiredAsync(\n        CancellationToken cancellationToken = default)\n    {\n        var now = DateTimeOffset.UtcNow;\n        return await db.Set<ManualLoginCredential>()\n            .Where(x => x.EncryptedPayload != null && x.ExpiresAt <= now)\n            .ExecuteUpdateAsync(setters => setters\n                .SetProperty(x => x.EncryptedPayload, (string?)null)\n                .SetProperty(x => x.ClearedAt, now)\n                .SetProperty(x => x.UpdatedAt, now),\n                cancellationToken);\n    }\n\n    public static void Clear(ManualLoginCredential? credential, DateTimeOffset now)\n'''
new = '''    public static bool IsUsable(ManualLoginCredential? credential, DateTimeOffset now) =>\n        credential is not null &&\n        !string.IsNullOrWhiteSpace(credential.EncryptedPayload) &&\n        credential.ExpiresAt > now;\n\n    public async Task<int> ClearExpiredAsync(\n        CancellationToken cancellationToken = default)\n    {\n        var now = DateTimeOffset.UtcNow;\n        var credentials = await db.Set<ManualLoginCredential>()\n            .Include(x => x.OrderItem)\n                .ThenInclude(x => x.Order)\n            .Where(x => x.EncryptedPayload != null && x.ExpiresAt <= now)\n            .ToListAsync(cancellationToken);\n\n        foreach (var credential in credentials)\n        {\n            Clear(credential, now);\n\n            var item = credential.OrderItem;\n            var order = item.Order;\n            if (item.FulfillmentMethod != FulfillmentMethod.MANUAL_LOGIN ||\n                order.PaymentStatus != PaymentStatus.Paid ||\n                item.FulfillmentStatus is FulfillmentStatus.Completed or FulfillmentStatus.Cancelled)\n            {\n                continue;\n            }\n\n            item.FulfillmentStatus = FulfillmentStatus.Failed;\n            item.FulfillmentStartedAt ??= order.PaidAt ?? now;\n            item.FulfilledAt = null;\n            item.FulfillmentMessage = "Login credentials expired before fulfillment could be completed.";\n\n            if (order.Status != OrderStatus.Cancelled)\n            {\n                order.Status = OrderStatus.Processing;\n                order.CompletedAt = null;\n                order.UpdatedAt = now;\n            }\n        }\n\n        if (credentials.Count > 0)\n        {\n            await db.SaveChangesAsync(cancellationToken);\n        }\n\n        return credentials.Count;\n    }\n\n    public static void Clear(ManualLoginCredential? credential, DateTimeOffset now)\n'''
if old not in text:
    raise SystemExit('ClearExpiredAsync anchor not found')
text = text.replace(old, new, 1)
path.write_text(text)

# 3) Starting a paid order must never leave a MANUAL_LOGIN item Processing when
# its credential is already absent/expired (e.g. late provider webhook).
path = Path('src/Zetruv.Api/Features/Orders/OrderFulfillmentService.cs')
text = path.read_text()
old = '''        foreach (var item in order.Items.Where(x => x.FulfillmentStatus == FulfillmentStatus.Pending))\n        {\n            item.FulfillmentStatus = FulfillmentStatus.Processing;\n            item.FulfillmentStartedAt ??= now;\n            item.FulfilledAt = null;\n            item.FulfillmentMessage = null;\n        }\n'''
new = '''        foreach (var item in order.Items.Where(x => x.FulfillmentStatus == FulfillmentStatus.Pending))\n        {\n            item.FulfillmentStartedAt ??= now;\n            item.FulfilledAt = null;\n\n            if (item.FulfillmentMethod == FulfillmentMethod.MANUAL_LOGIN &&\n                !ManualLoginCredentialService.IsUsable(item.ManualLoginCredential, now))\n            {\n                item.FulfillmentStatus = FulfillmentStatus.Failed;\n                item.FulfillmentMessage = "Login credentials expired or were cleared before payment was confirmed.";\n                continue;\n            }\n\n            item.FulfillmentStatus = FulfillmentStatus.Processing;\n            item.FulfillmentMessage = null;\n        }\n'''
if old not in text:
    raise SystemExit('StartPaidOrder anchor not found')
path.write_text(text.replace(old, new, 1))

# 4) Manual payment reconciliation in CMS also needs credentials loaded before
# StartPaidOrder evaluates MANUAL_LOGIN items.
path = Path('src/Zetruv.Api/Features/Orders/OrderControllers.cs')
text = path.read_text()
old = '''        var order = await db.Orders\n            .Include(x => x.Items)\n            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);\n\n        if (order is null)\n'''
new = '''        var order = await db.Orders\n            .Include(x => x.Items)\n                .ThenInclude(x => x.ManualLoginCredential)\n            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);\n\n        if (order is null)\n'''
if old not in text:
    raise SystemExit('OrderControllers payment include anchor not found')
path.write_text(text.replace(old, new, 1))

# 5) Order lookup: don't issue a payment recovery token when MANUAL_LOGIN
# credentials are already unusable.
path = Path('src/Zetruv.Api/Features/Orders/OrderTracking.cs')
text = path.read_text()
old = '''                x.Status != OrderStatus.Cancelled &&\n                    (x.PaymentStatus == PaymentStatus.Pending || x.PaymentStatus == PaymentStatus.Failed),\n                x.Transactions.Any(t =>\n'''
new = '''                x.Status != OrderStatus.Cancelled &&\n                    (x.PaymentStatus == PaymentStatus.Pending || x.PaymentStatus == PaymentStatus.Failed) &&\n                    x.Items.All(i =>\n                        i.FulfillmentMethod != FulfillmentMethod.MANUAL_LOGIN ||\n                        (i.ManualLoginCredential != null &&\n                         i.ManualLoginCredential.EncryptedPayload != null &&\n                         i.ManualLoginCredential.ExpiresAt > now)),\n                x.Transactions.Any(t =>\n'''
if old not in text:
    raise SystemExit('OrderTracking eligibility anchor not found')
path.write_text(text.replace(old, new, 1))

# 6) Fulfillment queue includes credential deadline/reveal metadata so admin UX
# can communicate urgency without revealing the secret itself.
path = Path('src/Zetruv.Api/Features/Orders/OrderModels.cs')
text = path.read_text()
old = '''    bool HasManualLoginCredentials,\n    IReadOnlyList<string>? ManualLoginCredentialFields,\n    string? FulfillmentReference,\n'''
new = '''    bool HasManualLoginCredentials,\n    IReadOnlyList<string>? ManualLoginCredentialFields,\n    DateTimeOffset? ManualLoginCredentialExpiresAt,\n    DateTimeOffset? ManualLoginCredentialLastRevealedAt,\n    string? FulfillmentReference,\n'''
if old not in text:
    raise SystemExit('FulfillmentQueueItemResponse anchor not found')
path.write_text(text.replace(old, new, 1))

path = Path('src/Zetruv.Api/Features/Orders/FulfillmentExecution.cs')
text = path.read_text()
old = '''                ManualLoginCredentialFieldsJson = x.ManualLoginCredential == null\n                    ? null\n                    : x.ManualLoginCredential.FieldNamesJson,\n                x.FulfillmentReference,\n'''
new = '''                ManualLoginCredentialFieldsJson = x.ManualLoginCredential == null\n                    ? null\n                    : x.ManualLoginCredential.FieldNamesJson,\n                ManualLoginCredentialExpiresAt = x.ManualLoginCredential == null\n                    ? null\n                    : (DateTimeOffset?)x.ManualLoginCredential.ExpiresAt,\n                ManualLoginCredentialLastRevealedAt = x.ManualLoginCredential == null\n                    ? null\n                    : x.ManualLoginCredential.LastRevealedAt,\n                x.FulfillmentReference,\n'''
if old not in text:
    raise SystemExit('Fulfillment queue projection anchor not found')
text = text.replace(old, new, 1)

old = '''            x.HasManualLoginCredentials\n                ? ManualLoginCredentialService.ParseFieldNames(\n                    x.ManualLoginCredentialFieldsJson)\n                : null,\n            x.FulfillmentReference,\n'''
new = '''            x.HasManualLoginCredentials\n                ? ManualLoginCredentialService.ParseFieldNames(\n                    x.ManualLoginCredentialFieldsJson)\n                : null,\n            x.ManualLoginCredentialExpiresAt,\n            x.ManualLoginCredentialLastRevealedAt,\n            x.FulfillmentReference,\n'''
if old not in text:
    raise SystemExit('Fulfillment queue response anchor not found')
path.write_text(text.replace(old, new, 1))

print('manual login lifecycle source patches applied')
